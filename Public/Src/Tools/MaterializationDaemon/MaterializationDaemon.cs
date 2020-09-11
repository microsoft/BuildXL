// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.CLI;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using MaterializationDaemon;
using Newtonsoft.Json;
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
        /// <nodoc/>
        public const string MaterializationDaemonLogPrefix = "(MMD) ";
        private const string LogFileName = "MaterializationDaemon";

        private static readonly TimeSpan s_externalProcessTimeout = TimeSpan.FromSeconds(10);

        private readonly MaterializationDaemonConfig m_config;
        private readonly Dictionary<string, string> m_macros;
        private readonly ActionQueue m_actionQueue;
        private readonly ConcurrentBigMap<string, bool> m_materializationStatus;

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

        internal static readonly StrOption ManifestParserExeLocation = RegisterMaterializationConfigOption(new StrOption("parserExe")
        {
            ShortName = "pe",
            HelpText = "Location of an external manifest parser executable",
            IsRequired = false,
            DefaultValue = null,
        });

        internal static readonly StrOption ManifestParserAdditionalArgs = RegisterMaterializationConfigOption(new StrOption("parserExeArgs")
        {
            ShortName = "pea",
            HelpText = "Additional command line arguments to include when launching a parser",
            IsRequired = false,
            DefaultValue = null,
        });

        internal static readonly StrOption DirectoryContentFilter = RegisterMaterializationConfigOption(new StrOption("directoryFilter")
        {
            ShortName = "dcf",
            HelpText = "Directory content filter (only files that match the filter are considered to be manifest files).",
            DefaultValue = null,
            IsRequired = false,
            IsMultiValue = true,
        });

        internal static MaterializationDaemonConfig CreateMaterializationDaemonConfig(ConfiguredCommand conf)
        {
            return new MaterializationDaemonConfig(
                maxDegreeOfParallelism: conf.Get(MaxDegreeOfParallelism),
                parserExe: conf.Get(ManifestParserExeLocation),
                parserArgs: conf.Get(ManifestParserAdditionalArgs),
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
           description: "Finds manifest files inside of a list of sealed directories, parses them, and requests materialization of needed output files",
           options: new Option[] { IpcServerMonikerRequired, Directory, DirectoryId, DirectoryContentFilter },
           clientAction: SyncRPCSend,
           serverAction: async (conf, daemon) =>
           {
               var materializationDaemon = daemon as MaterializationDaemon;
               materializationDaemon.Logger.Verbose("[REGISTERMANIFEST] Started");
               var result = await materializationDaemon.RegisterManifestInternalAsync(conf);
               materializationDaemon.Logger.Verbose("[REGISTERMANIFEST] " + result);
               return result;
           });

        internal static readonly Command FinalizeDropCmd = RegisterCommand(
            name: "finalize",
            description: "Logs final file materialization statuses",
            clientAction: SyncRPCSend,
            serverAction: (conf, daemon) =>
            {
                var materializationDaemon = daemon as MaterializationDaemon;
                materializationDaemon.Logger.Verbose("[FINALIZE] Started");
                materializationDaemon.LogMaterializationStatuses();
                materializationDaemon.Logger.Verbose("[FINALIZE] Finished");
                return Task.FromResult(IpcResult.Success());
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
            m_config = materializationConfig;
            m_actionQueue = new ActionQueue(m_config.MaxDegreeOfParallelism);
            m_materializationStatus = new ConcurrentBigMap<string, bool>();

            m_macros = new Dictionary<string, string>
            {
                ["$(build.nttree)"] = Environment.GetEnvironmentVariable("_NTTREE")
            };

            m_logger.Info($"MaterializationDaemon config: {JsonConvert.SerializeObject(m_config)}");
            m_logger.Info($"Defined macros (count={m_macros.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, m_macros.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

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

        /// <summary>
        /// Takes a list of sealed directories, searches their contents for special manifest files, parses each of them to obtain
        /// a list of files to materialize, and finally requests from BuildXL's ApiServer to materialize them.
        /// </summary>
        private async Task<IIpcResult> RegisterManifestInternalAsync(ConfiguredCommand conf)
        {
            var directoryPaths = Directory.GetValues(conf.Config).ToArray();
            var directoryIds = DirectoryId.GetValues(conf.Config).ToArray();
            var directoryFilters = DirectoryContentFilter.GetValues(conf.Config).ToArray();

            if (directoryPaths.Length != directoryIds.Length || directoryPaths.Length != directoryFilters.Length)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    I($"Directory counts don't match: #directories = {directoryPaths.Length}, #directoryIds = {directoryIds.Length}, #directoryFilters = {directoryFilters.Length}"));
            }

            if (ApiClient == null)
            {
                return new IpcResult(IpcResultStatus.GenericError, "ApiClient is not initialized");
            }

            (Regex[] initializedFilters, string filterInitError) = InitializeFilters(directoryFilters);
            if (filterInitError != null)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, filterInitError);
            }

            (List<SealedDirectoryFile> manifestFiles, string error) = await CollectManifestFilesAsync(directoryPaths, directoryIds, initializedFilters);
            if (error != null)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, error);
            }

            List<string> filesToMaterialize = null;
            try
            {
                // ensure that the manifest files are actually present on disk
                await m_actionQueue.ForEachAsync(
                    manifestFiles,
                    async (manifest, i) =>
                    {
                        await MaterializeFileAsync(manifest.Artifact, manifest.FileName, ignoreMaterializationFailures: false);
                    });

                (List<string> files, string parsingError) = await ParseManifestFilesAsync(manifestFiles);
                if (parsingError != null)
                {
                    return new IpcResult(IpcResultStatus.ExecutionError, parsingError);
                }

                filesToMaterialize = files;

                await m_actionQueue.ForEachAsync(
                    filesToMaterialize,
                    async (fileName, i) =>
                    {
                        // Since these file paths are not real build artifacts (i.e., just strings constructed by a parser),
                        // they might not be "known" to BuildXL, and because of that, BuildXL won't be able to materialize them.
                        // Manifests are known to contain references to files that are not produced during a build, so we are 
                        // ignoring materialization failures for such files. We are doing this so we won't fail IPC pips and
                        // a build could succeed.
                        // If there are real materialization errors (e.g., cache failure), the daemon relies on BuildXL logging
                        // the appropriate error events and failing the build.
                        await MaterializeFileAsync(FileArtifact.Invalid, fileName, ignoreMaterializationFailures: true);
                    });
            }
            catch (Exception e)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    e.ToStringDemystified());
            }

            return IpcResult.Success(
                $"Materialized files ({filesToMaterialize.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, filesToMaterialize)}");
        }

        private async Task<(List<SealedDirectoryFile>, string error)> CollectManifestFilesAsync(string[] directoryPaths, string[] directoryIds, Regex[] contentFilters)
        {
            var extractManifestsTasks = Enumerable
                .Range(0, directoryPaths.Length)
                .Select(i => GetManifestsFromDirectoryAsync(directoryPaths[i], directoryIds[i], contentFilters[i])).ToArray();

            var extractManifestsResults = await TaskUtilities.SafeWhenAll(extractManifestsTasks);
            if (extractManifestsResults.Any(r => r.error != null))
            {
                return (null, string.Join("; ", extractManifestsResults.Where(r => r.error != null).Select(r => r.error)));
            }

            return (extractManifestsResults.SelectMany(r => r.Item1).ToList(), null);
        }

        private async Task<(List<SealedDirectoryFile>, string error)> GetManifestsFromDirectoryAsync(
            string directoryPath,
            string directoryId,
            Regex contentFilter)
        {
            var directoryArtifact = BuildXL.Ipc.ExternalApi.DirectoryId.Parse(directoryId);
            var possibleContent = await ApiClient.GetSealedDirectoryContent(directoryArtifact, directoryPath);
            if (!possibleContent.Succeeded)
            {
                return (null, I($"Failed to get the content of a directory artifact ({directoryId}, {directoryPath}){Environment.NewLine}{possibleContent.Failure.Describe()}"));
            }

            var content = possibleContent.Result;
            Logger.Verbose($"(dirPath'{directoryPath}', dirId='{directoryId}') contains '{content.Count}' files:{Environment.NewLine}{string.Join(Environment.NewLine, content.Select(f => f.Render()))}");

            if (contentFilter != null)
            {
                var filteredContent = content.Where(file => contentFilter.IsMatch(file.FileName)).ToList();
                Logger.Verbose("[dirId='{0}'] Filter '{1}' excluded {2} file(s) out of {3}", directoryId, contentFilter, content.Count - filteredContent.Count, content.Count);
                content = filteredContent;
            }

            return (content, null);
        }

        private async Task<(List<string>, string error)> ParseManifestFilesAsync(List<SealedDirectoryFile> manifestFiles)
        {
            var parseManifestTasks = Enumerable
                .Range(0, manifestFiles.Count)
                .Select(i => m_config.ParserExeLocation != null
                    ? ParseManifestFileUsingExtenalParserAsync(manifestFiles[i].FileName)
                    // internal parsing is a temporary thing - it will be removed once an external parser is ready
                    : ParseManifestFileUsingInternaParser(manifestFiles[i].FileName))
                .ToArray();

            var parseManifestResults = await TaskUtilities.SafeWhenAll(parseManifestTasks);
            if (parseManifestResults.Any(r => r.error != null))
            {
                return (null, string.Join("; ", parseManifestResults.Where(r => r.error != null).Select(r => r.error)));
            }

            return (parseManifestResults.SelectMany(r => r.Item1).ToList(), null);
        }

        private async Task<(List<string>, string error)> ParseManifestFileUsingExtenalParserAsync(string manifestFilePath)
        {
            var process = CreateParserProcess(manifestFilePath);

            try
            {
                var result = new List<string>();
                using (var pooledList = Pools.GetStringList())
                {
                    var stdErrContent = pooledList.Instance;

                    using (var processExecutor = new AsyncProcessExecutor(
                        process,
                        s_externalProcessTimeout,
                        outputBuilder: line => { if (line != null) { result.Add(line); } },
                        errorBuilder: line => { if (line != null) { stdErrContent.Add(line); } }))
                    {
                        processExecutor.Start();
                        await processExecutor.WaitForExitAsync();
                        await processExecutor.WaitForStdOutAndStdErrAsync();

                        if (processExecutor.Process.ExitCode != 0)
                        {
                            var stdOut = $"{Environment.NewLine}{string.Join(Environment.NewLine, result)}";
                            var stdErr = $"{Environment.NewLine}{string.Join(Environment.NewLine, stdErrContent)}";
                            return (null, $"[Parser ('{process.StartInfo.FileName} {process.StartInfo.Arguments}')] Process failed with an exit code {processExecutor.Process.ExitCode}{stdOut}{stdErr}");
                        }

                        if (!int.TryParse(result[0], out var expectedCount))
                        {
                            var stdOut = $"{Environment.NewLine}{string.Join(Environment.NewLine, result)}";
                            return (null, $"[Parser ('{process.StartInfo.FileName} {process.StartInfo.Arguments}')] Failed to parse tool output {stdOut}");
                        }

                        if (expectedCount != result.Count - 1)
                        {
                            var stdOut = $"{Environment.NewLine}{string.Join(Environment.NewLine, result)}";
                            return (null, $"[Parser ('{process.StartInfo.FileName} {process.StartInfo.Arguments}')] Output line count does not match the expected count {stdOut}");
                        }

                        result.RemoveAt(0);
                        Logger.Verbose($"Manifest file ('{manifestFilePath}') content:{Environment.NewLine}{string.Join(Environment.NewLine, result)}");

                        return (result, null);
                    }
                }
            }
            catch (Exception e)
            {
                return (null, e.DemystifyToString());
            }
        }

        private Process CreateParserProcess(string manifestFilePath)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = m_config.ParserExeLocation,
                    Arguments = $"/i:\"{manifestFilePath}\" {m_config.ParserAdditionalCommandLineArguments}",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },

                EnableRaisingEvents = true
            };
        }

        private Task<(List<string>, string error)> ParseManifestFileUsingInternaParser(string manifestFilePath)
        {
            try
            {
                var parser = new XmlManifestParser(manifestFilePath, m_macros);
                var files = parser.ExtractFiles();
                Logger.Verbose($"Manifest file ('{manifestFilePath}') content:{Environment.NewLine}{string.Join(Environment.NewLine, files)}");

                return Task.FromResult<(List<string>, string error)>((files, null));
            }
            catch (Exception e)
            {
                return Task.FromResult<(List<string>, string error)>((null, e.DemystifyToString()));
            }
        }

        private async Task MaterializeFileAsync(FileArtifact fileArtifact, string filePath, bool ignoreMaterializationFailures)
        {
            try
            {
                Possible<bool> possibleResult = await ApiClient.MaterializeFile(fileArtifact, filePath);
                if (!possibleResult.Succeeded)
                {
                    throw new DaemonException(possibleResult.Failure.Describe());
                }

                if (!possibleResult.Result && !ignoreMaterializationFailures)
                {
                    throw new DaemonException($"Failed to materialize file: '{filePath}'");
                }

                if (possibleResult.Result && !System.IO.File.Exists(filePath))
                {
                    throw new DaemonException($"File materialization succeeded, but file was not found on disk: {filePath}");
                }

                m_materializationStatus.AddOrUpdate(
                    filePath,
                    possibleResult.Result,
                    (_, value) => value,
                    // if the file was successfully materialized before this call, keep the successful materialization status
                    (_, value, oldValue) => value || oldValue);
            }
            catch
            {
                m_materializationStatus.AddOrUpdate(
                    filePath,
                    false,
                    (_, value) => value,
                    // if the file was successfully materialized before this call, keep the successful materialization status
                    (_, value, oldValue) => value || oldValue);

                throw;
            }
        }

        private void LogMaterializationStatuses()
        {
            var pathsByStatus = m_materializationStatus.Keys.ToLookup(path => m_materializationStatus[path]);
            var materializedPaths = pathsByStatus[true].ToList();
            var failedPaths = pathsByStatus[false].ToList();

            Logger.Verbose("Successfully materialized {0} files:{1}{2}",
                materializedPaths.Count,
                Environment.NewLine,
                string.Join(Environment.NewLine, materializedPaths));

            if (failedPaths.Count > 0)
            {
                Logger.Warning("Failed to materialize {0} files:{1}{2}",
                    failedPaths.Count,
                    Environment.NewLine,
                    string.Join(Environment.NewLine, failedPaths));
            }
        }
    }
}
