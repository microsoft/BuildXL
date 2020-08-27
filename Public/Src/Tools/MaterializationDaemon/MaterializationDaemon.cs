// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.CLI;
using Tool.ServicePipDaemon;
using static BuildXL.Utilities.FormattableStringEx;

namespace Tool.MaterializationDaemon
{
    /// <summary>
    /// The daemon responsible for "on demand" file materialization on the orchestrator machine.
    /// The daemon takes manifest files produced during a build and triggers materialization of
    /// all files referenced by those manifests. By using this daemon, builds can limit the number 
    /// of materialized files.
    /// </summary>
    public sealed class MaterializationDaemon : ServicePipDaemon.ServicePipDaemon, IIpcOperationExecutor
    {
        private const string LogFileName = "MaterializationDaemon";
        private static readonly int s_minIoThreads = Environment.ProcessorCount * 10;
        private static readonly int s_minWorkerThreads = Environment.ProcessorCount * 10;

        /// <nodoc/>
        public const string MaterializationDaemonLogPrefix = "(MMD) ";

        #region Options and commands

        internal static readonly List<Option> MaterializationConfigOptions = new List<Option>();

        private static T RegisterMaterializationConfigOption<T>(T option) where T : Option => RegisterOption(MaterializationConfigOptions, option);

        internal static readonly IntOption MaxDegreeOfParallelism = RegisterMaterializationConfigOption(new IntOption("maxDegreeOfParallelism")
        {
            ShortName = "mdp",
            HelpText = "Maximum number of files to materialize concurrently",
            IsRequired = false,
            DefaultValue = MaterializationDaemonConfig.DefaultMaxDegreeOfParallelism,
        });

        internal static MaterializationDaemonConfig CreateMaterializationDaemonConfig(ConfiguredCommand conf)
        {
            return new MaterializationDaemonConfig(
                maxDegreeOfParallelism: conf.Get(MaxDegreeOfParallelism),
                logDir: conf.Get(LogDir));
        }

        private static Client CreateClient(string serverMoniker, IClientConfig config)
        {
            return serverMoniker != null
                ? Client.Create(serverMoniker, config)
                : null;
        }

        internal static readonly Command StartCmd = RegisterCommand(
            name: "start",
            description: "Starts the server process.",
            options: MaterializationConfigOptions.Union(new[] { IpcServerMonikerOptional }),
            needsIpcClient: false,
            clientAction: (conf, _) =>
            {
                var materializationConfig = CreateMaterializationDaemonConfig(conf);
                var daemonConf = CreateDaemonConfig(conf);

                if (daemonConf.MaxConcurrentClients <= 1)
                {
                    conf.Logger.Error($"Must specify at least 2 '{nameof(DaemonConfig.MaxConcurrentClients)}' when running daemon to avoid deadlock when stopping this daemon from a different client");
                    return -1;
                }

                using (var bxlApiClient = CreateClient(conf.Get(IpcServerMonikerOptional), daemonConf))
                using (var daemon = new MaterializationDaemon(
                    parser: conf.Config.Parser,
                    daemonConfig: daemonConf,
                    materializationConfig: materializationConfig,
                    bxlClient: bxlApiClient))
                {
                    daemon.Start();
                    // We are blocking the thread here and waiting for the daemon to process all the requests.
                    // Once the daemon receives 'stop' command, GetResult will return, and we'll leave this method
                    // (i.e., ServicePip will finish).
                    daemon.Completion.GetAwaiter().GetResult();
                    return 0;
                }
            });

        internal static readonly Command RegisterManifestCmd = RegisterCommand(
           name: "registerManifest",
           description: "Reads manifest files and requests materialization of needed output files",
           options: new Option[] { IpcServerMonikerRequired, Directory, DirectoryId },
           clientAction: SyncRPCSend,
           serverAction: async (conf, daemon) =>
           {
               var materializationDaemon = daemon as MaterializationDaemon;
               materializationDaemon.Logger.Verbose("[registerManifest] Started");
               var result = await RegisterManifestInternalAsync(conf, materializationDaemon);
               materializationDaemon.Logger.Verbose("[registerManifest] " + result);
               return result;
           });

        #endregion

        static MaterializationDaemon()
        {
            // noop (used to force proper initialization of static fields)
        }

        /// <nodoc/>
        public MaterializationDaemon(
            IParser parser,
            DaemonConfig daemonConfig,
            MaterializationDaemonConfig materializationConfig,
            IIpcProvider rpcProvider = null,
            Client bxlClient = null)
                : base(parser,
                      daemonConfig,
                      !string.IsNullOrWhiteSpace(materializationConfig.LogDir) ? new FileLogger(materializationConfig.LogDir, LogFileName, daemonConfig.Moniker, logVerbose: true, MaterializationDaemonLogPrefix) : daemonConfig.Logger,
                      rpcProvider,
                      bxlClient)
        {
        }

        internal static void EnsureCommandsInitialized()
        {
            Contract.Assert(Commands != null);

            // these operations are quite expensive, however, we expect to call this method only once during a build, so it should not cause any perf downgrade
            var numCommandsBase = countFields(typeof(ServicePipDaemon.ServicePipDaemon));
            var numCommandsMaterializationDaemon = countFields(typeof(MaterializationDaemon));

            if (Commands.Count != numCommandsBase + numCommandsMaterializationDaemon)
            {
                Contract.Assert(false, $"The list of commands was not properly initialized (# of initialized commands = {Commands.Count}; # of ServicePipDaemon commands = {numCommandsBase}; # of MaterializationDaemon commands = {numCommandsMaterializationDaemon})");
            }

            int countFields(Type type)
            {
                return type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Command)).Count();
            }
        }

        private static async Task<IIpcResult> RegisterManifestInternalAsync(ConfiguredCommand conf, MaterializationDaemon daemon)
        {
            var directoryPaths = Directory.GetValues(conf.Config).ToArray();
            var directoryIds = DirectoryId.GetValues(conf.Config).ToArray();

            if (directoryPaths.Length != directoryIds.Length)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    I($"Directory counts don't match: #directories = {directoryPaths.Length}, #directoryIds = {directoryIds.Length}"));
            }

            if (daemon.ApiClient == null)
            {
                return new IpcResult(IpcResultStatus.GenericError, "ApiClient is not initialized");
            }

            var manifests = new List<SealedDirectoryFile>();
            for (int i = 0; i < directoryIds.Length; i++)
            {
                var directoryArtifact = BuildXL.Ipc.ExternalApi.DirectoryId.Parse(directoryIds[i]);
                var possibleContent = await daemon.ApiClient.GetSealedDirectoryContent(directoryArtifact, directoryPaths[i]);
                if (!possibleContent.Succeeded)
                {
                    return new IpcResult(
                        IpcResultStatus.GenericError,
                        I($"Failed to get the content of a directory artifact ({directoryIds[i]}, {directoryPaths[i]}){Environment.NewLine}{possibleContent.Failure.DescribeIncludingInnerFailures()}"));
                }

                manifests.AddRange(possibleContent.Result);
            }

            daemon.Logger.Verbose(string.Join(Environment.NewLine, manifests.Select(f => f.FileName)));

            // TODO: placeholder for now - to be implemented in another pr
            return IpcResult.Success("done");
        }
    }
}
