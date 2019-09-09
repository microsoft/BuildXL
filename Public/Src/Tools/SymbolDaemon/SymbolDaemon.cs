// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Storage;
using BuildXL.Utilities.CLI;
using BuildXL.Utilities.Tasks;
using Microsoft.VisualStudio.Services.Symbol.App.Core.Tracing;
using Microsoft.VisualStudio.Services.Symbol.WebApi;
using Newtonsoft.Json;
using Tool.ServicePipDaemon;
using static BuildXL.Utilities.FormattableStringEx;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// Daemon responsible for handling symbol-related requests.
    /// </summary>
    public sealed class SymbolDaemon : ServicePipDaemon.ServicePipDaemon, IDisposable, IIpcOperationExecutor
    {
        private const string LogFileName = "SymbolDaemon";
        private const int s_servicePointParallelism = 200;

        /// <nodoc/>
        public const string SymbolDLogPrefix = "(SymbolD) ";

        private static readonly int s_minIoThreads = Environment.ProcessorCount * 10;
        private static readonly int s_minWorkerThreads = Environment.ProcessorCount * 10;

        private Task<ISymbolClient> m_symbolServiceClientTask;
        private readonly ISymbolIndexer m_symbolIndexer;

        /// <summary>
        /// Configuration used to initialize this daemon
        /// </summary>
        public SymbolConfig SymbolConfig { get; }

        /// <nodoc />
        public string RequestName => SymbolConfig.Name;

        # region Symbol daemon options and commands

        internal static readonly List<Option> SymbolConfigOptions = new List<Option>();

        private static T RegisterSymbolConfigOption<T>(T option) where T : Option => RegisterOption(SymbolConfigOptions, option);

        internal static readonly StrOption SymbolRequestNameOption = RegisterSymbolConfigOption(new StrOption("name")
        {
            ShortName = "n",
            HelpText = "Request name",
            IsRequired = true,
        });

        internal static readonly UriOption ServiceEndpoint = RegisterSymbolConfigOption(new UriOption("service")
        {
            ShortName = "s",
            HelpText = "Symbol service endpoint URI",
            IsRequired = true,
        });

        internal static readonly IntOption RetentionDays = RegisterSymbolConfigOption(new IntOption("retentionDays")
        {
            ShortName = "rt",
            HelpText = "Symbol retention time in days",
            IsRequired = false,
            DefaultValue = (int)SymbolConfig.DefaultRetention.TotalDays,
        });

        internal static readonly IntOption HttpSendTimeoutMillis = RegisterSymbolConfigOption(new IntOption("httpSendTimeoutMillis")
        {
            HelpText = "Timeout for http requests",
            IsRequired = false,
            DefaultValue = (int)SymbolConfig.DefaultHttpSendTimeout.TotalMilliseconds,
        });

        internal static readonly BoolOption EnableTelemetry = RegisterSymbolConfigOption(new BoolOption("enableTelemetry")
        {
            ShortName = "t",
            HelpText = "Verbose logging",
            IsRequired = false,
            DefaultValue = SymbolConfig.DefaultEnableTelemetry,
        });

        internal static SymbolConfig CreateSymbolConfig(ConfiguredCommand conf)
        {
            return new SymbolConfig(
                requestName: conf.Get(SymbolRequestNameOption),
                serviceEndpoint: conf.Get(ServiceEndpoint),
                retention: TimeSpan.FromDays(conf.Get(RetentionDays)),
                httpSendTimeout: TimeSpan.FromMilliseconds(conf.Get(HttpSendTimeoutMillis)),
                verbose: conf.Get(Verbose),
                enableTelemetry: conf.Get(EnableTelemetry),
                logDir: conf.Get(LogDir));
        }

        private static Client CreateClient(string serverMoniker, IClientConfig config)
        {
            return serverMoniker != null
                ? Client.Create(serverMoniker, config)
                : null;
        }

        internal static readonly Command StartNoServiceCmd = RegisterCommand(
            name: "start-noservice",
            description: @"Starts a server process without a backing symbol service client (useful for testing/pinging the daemon).",
            needsIpcClient: false,
            clientAction: (conf, _) =>
            {
                var symbolConfig = new SymbolConfig(string.Empty, new Uri("file://xyz"));
                var daemonConfig = CreateDaemonConfig(conf);
                var vsoClientTask = TaskSourceSlim.Create<ISymbolClient>();
                vsoClientTask.SetException(new NotSupportedException());
                using (var daemon = new SymbolDaemon(conf.Config.Parser, daemonConfig, symbolConfig, vsoClientTask.Task))
                {
                    daemon.Start();
                    daemon.Completion.GetAwaiter().GetResult();
                    return 0;
                }
            });

        internal static readonly Command StartCmd = RegisterCommand(
           name: "start",
           description: "Starts the server process.",
           options: SymbolConfigOptions.Union(new[] { IpcServerMonikerOptional }),
           needsIpcClient: false,
           clientAction: (conf, _) =>
           {
               // This command is used when BXL creates a ServicePip for SymbolDaemon.
               SetupThreadPoolAndServicePoint(s_minWorkerThreads, s_minIoThreads, s_servicePointParallelism);
               var symbolConfig = CreateSymbolConfig(conf);
               var daemonConf = CreateDaemonConfig(conf);

               if (daemonConf.MaxConcurrentClients <= 1)
               {
                   conf.Logger.Error($"Must specify at least 2 '{nameof(DaemonConfig.MaxConcurrentClients)}' when running SymbolDaemon to avoid deadlock when stopping this daemon from a different client");
                   return -1;
               }

               using (var client = CreateClient(conf.Get(IpcServerMonikerOptional), daemonConf))
               using (var daemon = new SymbolDaemon(
                   parser: conf.Config.Parser,
                   daemonConfig: daemonConf,
                   symbolConfig: symbolConfig,
                   symbolServiceClientTask: null,
                   bxlClient: client))
               {
                   daemon.Start();                   
                   // We are blocking the thread here and waiting for the SymbolDaemon to process all the requests.
                   // Once the daemon receives 'stop' command, GetResult will return, and we'll leave this method
                   // (i.e., ServicePip will finish).
                   daemon.Completion.GetAwaiter().GetResult();
                   return 0;
               }
           });

        internal static readonly Command CreateSymbolRequestCmd = RegisterCommand(
           name: "create",
           description: "[RPC] Invokes the 'create' operation.",
           options: SymbolConfigOptions,
           clientAction: SyncRPCSend,
           serverAction: async (conf, daemon) =>
           {
               var symbolDaemon = daemon as SymbolDaemon;
               symbolDaemon.Logger.Info("[CREATE]: Started at " + symbolDaemon.SymbolConfig.Service + "/" + symbolDaemon.SymbolConfig.Name);
               IIpcResult result = await symbolDaemon.CreateAsync();
               daemon.Logger.Info("[CREATE]: " + result);
               return result;
           });

        internal static readonly Command FinalizeSymbolRequestCmd = RegisterCommand(
            name: "finalize",
            description: "[RPC] Invokes the 'finalize' operation.",
            clientAction: SyncRPCSend,
            serverAction: async (conf, daemon) =>
            {
                var symbolDaemon = daemon as SymbolDaemon;
                symbolDaemon.Logger.Info("[FINALIZE] Started at" + symbolDaemon.SymbolConfig.Service + "/" + symbolDaemon.SymbolConfig.Name);
                IIpcResult result = await symbolDaemon.FinalizeAsync();
                daemon.Logger.Info("[FINALIZE] " + result);
                return result;
            });

        internal static readonly Command FinalizeSymbolRequestAndStopDaemonCmd = RegisterCommand(
            name: "finalize-and-stop",
            description: "[RPC] Invokes the 'finalize' operation; then stops the daemon.",
            clientAction: SyncRPCSend,
            serverAction: Command.Compose(FinalizeSymbolRequestCmd.ServerAction, StopDaemonCmd.ServerAction));

        internal static readonly Command AddSymbolFilesCmd = RegisterCommand(
            name: "addsymbolfiles",
            description: "[RPC] invokes the 'addsymbolfiles' operation.",
            options: new Option[] { IpcServerMonikerRequired, File, FileId, HashOptional },
            clientAction: SyncRPCSend,
            serverAction: async (conf, daemon) =>
            {
                var symbolDaemon = daemon as SymbolDaemon;
                symbolDaemon.Logger.Verbose("[ADDSYMBOLS] Started");
                var result = await AddSymbolFilesInternalAsync(conf, symbolDaemon);
                symbolDaemon.Logger.Verbose("[ADDSYMBOLS] " + result);
                return result;
            });

        private static async Task<IIpcResult> AddSymbolFilesInternalAsync(ConfiguredCommand conf, SymbolDaemon daemon)
        {
            var files = File.GetValues(conf.Config).ToArray();
            var fileIds = FileId.GetValues(conf.Config).ToArray();
            var hashes = HashOptional.GetValues(conf.Config).ToArray();

            if (files.Length != fileIds.Length || files.Length != hashes.Length)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    I($"File counts don't match: #files = {files.Length}, #fileIds = {fileIds.Length}, #hashes = {hashes.Length}"));
            }

            var symbolFiles = Enumerable
                .Range(0, files.Length)
                .Select(i => new SymbolFile(
                    daemon.ApiClient,
                    files[i],
                    fileIds[i],
                    FileContentInfo.Parse(hashes[i]).Hash)).ToList();

            var result = await daemon.AddSymbolFilesAsync(symbolFiles);

            return result;
        }

        #endregion

        static SymbolDaemon()
        {
            // noop
        }

        /// <nodoc/>
        public SymbolDaemon(
            IParser parser,
            DaemonConfig daemonConfig,
            SymbolConfig symbolConfig,
            Task<ISymbolClient> symbolServiceClientTask,
            IIpcProvider rpcProvider = null,
            Client bxlClient = null)
                : base(parser,
                      daemonConfig,
                      !string.IsNullOrWhiteSpace(symbolConfig.LogDir) ? new FileLogger(symbolConfig.LogDir, LogFileName, daemonConfig.Moniker, symbolConfig.Verbose, SymbolDLogPrefix) : daemonConfig.Logger,
                      rpcProvider,
                      bxlClient)
        {
            Contract.Requires(symbolConfig != null);

            SymbolConfig = symbolConfig;
            m_logger.Info(I($"Using {nameof(DaemonConfig)}: {JsonConvert.SerializeObject(daemonConfig)}"));
            m_logger.Info(I($"Using {nameof(SymbolConfig)}: {JsonConvert.SerializeObject(symbolConfig)}"));

            // if no ISymbolServiceClient has been provided, create VsoSymbolClient using the provided SymbolConfig
            m_symbolServiceClientTask = symbolServiceClientTask ?? Task.Run(() => (ISymbolClient)new VsoSymbolClient(m_logger, symbolConfig));

            m_symbolIndexer = new SymbolIndexer(SymbolAppTraceSource.SingleInstance);
        }

        internal static void EnsureCommandsInitialized()
        {
            Contract.Assert(Commands != null);

            // these operations are quite expensive, however, we expect to call this method only once per symbol request, so it should cause any perf downgrade
            var numCommandsBase = typeof(ServicePipDaemon.ServicePipDaemon).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Command)).Count();
            var numCommandsSymbolD = typeof(SymbolDaemon).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Command)).Count();

            if (Commands.Count != numCommandsBase + numCommandsSymbolD)
            {
                Contract.Assert(false, $"The list of commands was not properly initialized (# of initialized commands = {Commands.Count}; # of ServicePipDaemon commands = {numCommandsBase}; # of SymbolDaemon commands = {numCommandsSymbolD})");
            }
        }

        /// <summary>
        /// Synchronous version of <see cref="CreateAsync"/>
        /// </summary>
        public IIpcResult Create()
        {
            return CreateAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a symbol request.
        /// </summary>
        public async Task<IIpcResult> CreateAsync()
        {
            // TODO(olkonone): add logging
            Request createRequestResult;

            try
            {
                createRequestResult = await InternalCreateAsync();
            }
            catch (Exception e)
            {
                return new IpcResult(IpcResultStatus.GenericError, e.DemystifyToString());
            }

            // get and return RequestId ?
            return IpcResult.Success(I($"Symbol request '{RequestName}' created (assigned request ID: '{createRequestResult.Id}')."));
        }

        /// <summary>
        /// Indexes the files and adds symbol data to the request.
        /// </summary>       
        public async Task<IIpcResult> AddSymbolFilesAsync(List<SymbolFile> files)
        {
            var result = await EnsureFilesAreIndexedAsync(files);

            if (!result.Success)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, result.Exception.DemystifyToString());
            }

            var addFileTasks = files.Select(f => AddSymbolFileAsync(f));
            var ipcResults = await TaskUtilities.SafeWhenAll(addFileTasks);

            return IpcResult.Merge(ipcResults);
        }

        private async Task<(bool Success, Exception Exception)> EnsureFilesAreIndexedAsync(List<SymbolFile> files)
        {
            try
            {
                foreach (var symbolFile in files)
                {
                    var fi = await symbolFile.EnsureMaterializedAsync();

                    var debugEntries = m_symbolIndexer.GetDebugEntries(fi, calculateBlobId: true).ToList();

                    symbolFile.SetDebugEntries(debugEntries);
                }

                return (true, null);
            }
            catch (Exception e)
            {
                return (false, e);
            }
        }

        private async Task<IIpcResult> AddSymbolFileAsync(SymbolFile file)
        {
            try
            {
                var symbolClient = await m_symbolServiceClientTask;
                var result = await symbolClient.AddFileAsync(file);

                return IpcResult.Success(I($"File '{file.FullFilePath}' {result} in request '{RequestName}'."));
            }
            catch (Exception e)
            {
                return new IpcResult(IpcResultStatus.GenericError, e.DemystifyToString());
            }
        }

        /// <summary>
        /// Synchronous version of <see cref="FinalizeAsync"/>
        /// </summary>
        public IIpcResult Finalize()
        {
            return FinalizeAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finalizes the symbol request. 
        /// </summary>
        public async Task<IIpcResult> FinalizeAsync()
        {
            // TODO(olkonone): add logging
            Request finalizeRequestResult;

            try
            {
                finalizeRequestResult = await InternalFinalizeAsync();
            }
            catch (Exception e)
            {
                return new IpcResult(IpcResultStatus.GenericError, e.DemystifyToString());
            }

            return IpcResult.Success(I($"Symbol request '{RequestName}' finalized; the request expires on '{finalizeRequestResult.ExpirationDate}'."));
        }

        /// <nodoc />
        public new void Dispose()
        {
            if (m_symbolServiceClientTask.IsCompleted && !m_symbolServiceClientTask.IsFaulted)
            {
                m_symbolServiceClientTask.Result.Dispose();
            }

            base.Dispose();
        }

        private async Task<Request> InternalCreateAsync()
        {
            var symbolClient = await m_symbolServiceClientTask;
            var result = await symbolClient.CreateAsync();

            Contract.Assert(result.Status == RequestStatus.Created);

            return result;
        }

        private async Task<Request> InternalFinalizeAsync()
        {
            var symbolClient = await m_symbolServiceClientTask;
            var result = await symbolClient.FinalizeAsync();

            Contract.Assert(result.Status == RequestStatus.Sealed);
            Contract.Assert(result.ExpirationDate.HasValue);

            return result;
        }
    }
}
