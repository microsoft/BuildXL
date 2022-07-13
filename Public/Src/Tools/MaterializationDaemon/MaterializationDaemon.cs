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
using BuildXL.Utilities.Tracing;
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
        private static readonly TimeSpan[] s_retryIntervals = new[]
        {
            // the values are similar to the ones in the ReloadingClient class
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(16),
            TimeSpan.FromSeconds(32),
        };

        private readonly MaterializationDaemonConfig m_config;
        private readonly Dictionary<string, string> m_macros;
        private readonly ActionQueue m_actionQueue;
        private readonly ConcurrentBigMap<string, bool> m_materializationStatus;
        private readonly CounterCollection<MaterializationDaemonCounter> m_counters;

        #region Options and commands

        internal static readonly List<Option> MaterializationConfigOptions = new List<Option>();

        private static T RegisterMaterializationConfigOption<T>(T option) where T : Option => RegisterOption(MaterializationConfigOptions, option);

        internal static readonly IntOption MaxDegreeOfParallelism = RegisterMaterializationConfigOption(new IntOption("maxDegreeOfParallelism")
        {
            ShortName = "mdp",
            HelpText = "Maximum number of files to materialize concurrently",
            IsRequired = true,
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
            IsRequired = true,
            IsMultiValue = true,
        });

        internal static readonly StrOption DirectoryContentFilterKind = RegisterMaterializationConfigOption(new StrOption("directoryFilterKind")
        {
            ShortName = "dcfk",
            HelpText = "Directory content filter kind (i.e., include/exclude).",
            DefaultValue = null,
            IsRequired = true,
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
                ? Client.Create(IpcProvider, serverMoniker, config)
                : null;
        }

        internal static readonly Command StartCmd = RegisterCommand(
            name: "start",
            description: "Starts the server process.",
            options: new Option[] { IpcServerMonikerOptional, MaxDegreeOfParallelism, ManifestParserExeLocation, ManifestParserAdditionalArgs },
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

        internal static readonly Command FinalizeCmd = RegisterCommand(
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

        internal static readonly Command MaterializeDirectoriesCmd = RegisterCommand(
            name: "materializeDirectories",
            description: "Materializes content of given output directories",
            options: new Option[] { IpcServerMonikerRequired, Directory, DirectoryId, DirectoryContentFilter, DirectoryContentFilterKind },
            clientAction: SyncRPCSend,
            serverAction: async (conf, daemon) =>
            {
                var materializationDaemon = daemon as MaterializationDaemon;
                materializationDaemon.Logger.Verbose("[MATERIALIZEDIRECTORIES] Started");
                var result = await materializationDaemon.MaterializeDirectoriesAsync(conf);
                materializationDaemon.Logger.Verbose("[MATERIALIZEDIRECTORIES] " + result);
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
            m_config = materializationConfig;
            m_actionQueue = new ActionQueue(m_config.MaxDegreeOfParallelism);
            m_materializationStatus = new ConcurrentBigMap<string, bool>();
            m_counters = new CounterCollection<MaterializationDaemonCounter>();

            m_macros = new Dictionary<string, string>
            {
                ["$(build.nttree)"] = Environment.GetEnvironmentVariable("_NTTREE")
            };

            m_logger.Info($"MaterializationDaemon config: {JsonConvert.SerializeObject(m_config)}");
            m_logger.Info($"Defined macros (count={m_macros.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, m_macros.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

        }

        /// <nodoc/>
        public override void Dispose()
        {
            ReportStatisticsAsync().GetAwaiter().GetResult();
            base.Dispose();
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
            m_counters.IncrementCounter(MaterializationDaemonCounter.RegisterManifestRequestCount);

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

            var possibleFilters = InitializeFilters(directoryFilters);
            if (!possibleFilters.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, possibleFilters.Failure.Describe());
            }

            var initializedFilters = possibleFilters.Result;
            var possibleManifestFiles = await GetUniqueFilteredDirectoryContentAsync(directoryPaths, directoryIds, initializedFilters);
            if (!possibleManifestFiles.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, possibleManifestFiles.Failure.Describe());
            }

            var manifestFiles = possibleManifestFiles.Result;
            var sw = Stopwatch.StartNew();
            List<string> filesToMaterialize = null;
            try
            {
                // ensure that the manifest files are actually present on disk
                using (m_counters.StartStopwatch(MaterializationDaemonCounter.RegisterManifestFileMaterializationDuration))
                {
                    await m_actionQueue.ForEachAsync(
                        manifestFiles,
                        async (manifest, i) =>
                        {
                            m_counters.AddToCounter(MaterializationDaemonCounter.RegisterManifestFileMaterializationQueueDuration, sw.ElapsedMilliseconds);
                            await MaterializeFileAsync(manifest.Artifact, manifest.FileName, ignoreMaterializationFailures: false);
                        }); 
                }

                Possible<List<string>> possibleFiles;
                using (m_counters.StartStopwatch(MaterializationDaemonCounter.ManifestParsingOuterDuration))
                {
                    m_counters.AddToCounter(MaterializationDaemonCounter.ManifestParsingTotalFiles, possibleManifestFiles.Result.Count);
                    possibleFiles = await ParseManifestFilesAsync(manifestFiles);
                    if (!possibleFiles.Succeeded)
                    {
                        return new IpcResult(IpcResultStatus.ExecutionError, possibleFiles.Failure.Describe());
                    }

                    m_counters.AddToCounter(MaterializationDaemonCounter.ManifestParsingReferencedTotalFiles, possibleFiles.Result.Count);
                }

                filesToMaterialize = possibleFiles.Result;
                sw = Stopwatch.StartNew();
                using (m_counters.StartStopwatch(MaterializationDaemonCounter.RegisterManifestReferencedFileMaterializationDuration))
                {
                    await m_actionQueue.ForEachAsync(
                        filesToMaterialize,
                        async (file, i) =>
                        {
                            // Since these file paths are not real build artifacts (i.e., just strings constructed by a parser),
                            // they might not be "known" to BuildXL, and because of that, BuildXL won't be able to materialize them.
                            // Manifests are known to contain references to files that are not produced during a build, so we are 
                            // ignoring materialization failures for such files. We are doing this so we won't fail IPC pips and
                            // a build could succeed.
                            // If there are real materialization errors (e.g., cache failure), the daemon relies on BuildXL logging
                            // the appropriate error events and failing the build.
                            m_counters.AddToCounter(MaterializationDaemonCounter.RegisterManifestReferencedFileMaterializationQueueDuration, sw.ElapsedMilliseconds);
                            await MaterializeFileAsync(FileArtifact.Invalid, file, ignoreMaterializationFailures: true);
                        }); 
                }
            }
            catch (Exception e)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    e.ToStringDemystified());
            }

            // Note: we are not claiming here that all the files in filesToMaterialize were materialized cause we are ignoring materialization failures.
            // The real materialization status will be logged during execution of the Finalize command.
            return IpcResult.Success(
                $"Processed paths ({filesToMaterialize.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, filesToMaterialize)}");
        }

        private async Task<IIpcResult> MaterializeDirectoriesAsync(ConfiguredCommand conf)
        {
            m_counters.IncrementCounter(MaterializationDaemonCounter.MaterializeDirectoriesRequestCount);

            var directoryPaths = Directory.GetValues(conf.Config).ToArray();
            var directoryIds = DirectoryId.GetValues(conf.Config).ToArray();
            var directoryFilters = DirectoryContentFilter.GetValues(conf.Config).ToArray();
            var directoryFilterKinds = DirectoryContentFilterKind.GetValues(conf.Config).ToArray();

            if (directoryPaths.Length != directoryIds.Length || directoryPaths.Length != directoryFilters.Length || directoryPaths.Length != directoryFilterKinds.Length)
            {
                return new IpcResult(
                    IpcResultStatus.InvalidInput,
                    I($"Directory counts don't match: #directories = {directoryPaths.Length}, #directoryIds = {directoryIds.Length}, #directoryFilters = {directoryFilters.Length}, #directoryFilterKinds = {directoryFilterKinds.Length}"));
            }

            if (ApiClient == null)
            {
                return new IpcResult(IpcResultStatus.GenericError, "ApiClient is not initialized");
            }

            var possibleFilters = InitializeFilters(directoryFilters);
            if (!possibleFilters.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, possibleFilters.Failure.Describe());
            }

            var possibleFilterKinds = ParseFilterKinds(directoryFilterKinds);
            if (!possibleFilterKinds.Succeeded)
            {
                return new IpcResult(IpcResultStatus.InvalidInput, possibleFilterKinds.Failure.Describe());
            }

            var initializedFilters = possibleFilters.Result;
            var filterKinds = possibleFilterKinds.Result;
            var possibleFiles = await GetUniqueFilteredDirectoryContentAsync(directoryPaths, directoryIds, initializedFilters, filterKinds);
            if (!possibleFiles.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, possibleFiles.Failure.Describe());
            }

            var filesToMaterialize = possibleFiles.Result;
            var sw = Stopwatch.StartNew();
            using (m_counters.StartStopwatch(MaterializationDaemonCounter.MaterializeDirectoriesOuterMaterializationDuration))
            {
                try
                {
                    await m_actionQueue.ForEachAsync(
                       filesToMaterialize,
                       async (file, i) =>
                       {
                           m_counters.AddToCounter(MaterializationDaemonCounter.MaterializeDirectoriesMaterializationQueueDuration, sw.ElapsedMilliseconds);
                           await MaterializeFileAsync(file.Artifact, file.FileName, ignoreMaterializationFailures: false);
                       });
                }
                catch (Exception e)
                {
                    return new IpcResult(
                       IpcResultStatus.GenericError,
                       e.ToStringDemystified());
                }
            }

            m_counters.IncrementCounter(MaterializationDaemonCounter.MaterializeDirectoriesFilesToMaterialize);
            return IpcResult.Success(
                $"Materialized files ({filesToMaterialize.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, filesToMaterialize.Select(f => f.FileName))}");
        }

        /// <summary>
        /// Collects filtered content of all directories and dedupes the list before returning it.
        /// </summary>
        private async Task<Possible<List<SealedDirectoryFile>>> GetUniqueFilteredDirectoryContentAsync(
            string[] directoryPaths,
            string[] directoryIds,
            Regex[] contentFilters,
            FilterKind[] filterKinds = null)
        {
            // TODO: add counter to measure the time spent getting dir content
            var getContentTasks = Enumerable
                .Range(0, directoryPaths.Length)
                .Select(i => GetDirectoryContentAndApplyFilterAsync(directoryPaths[i], directoryIds[i], contentFilters[i], filterKinds?[i] ?? FilterKind.Include)).ToArray();

            var getContentResults = await TaskUtilities.SafeWhenAll(getContentTasks);
            if (getContentResults.Any(r => !r.Succeeded))
            {
                return new Failure<string>(string.Join("; ", getContentResults.Where(r => !r.Succeeded).Select(r => r.Failure.Describe())));
            }

            // dedupe collected files using file artifact as a key
            var dedupedFiles = getContentResults.SelectMany(res => res.Result).GroupBy(file => file.Artifact).Select(group => group.First());

            return dedupedFiles.ToList();
        }

        private async Task<Possible<List<SealedDirectoryFile>>> GetDirectoryContentAndApplyFilterAsync(
            string directoryPath,
            string directoryId,
            Regex contentFilter,
            FilterKind filterKind)
        {
            var directoryArtifact = BuildXL.Ipc.ExternalApi.DirectoryId.Parse(directoryId);
            // TODO: Consider doing filtering on the engine side
            Possible<List<SealedDirectoryFile>> possibleContent;
            using (m_counters.StartStopwatch(MaterializationDaemonCounter.GetSealedDirectoryContentDuration))
            {
                possibleContent = await ApiClient.GetSealedDirectoryContent(directoryArtifact, directoryPath);
                if (!possibleContent.Succeeded)
                {
                    return new Failure<string>(I($"Failed to get the content of a directory artifact ({directoryId}, {directoryPath}){Environment.NewLine}{possibleContent.Failure.Describe()}"));
                } 
            }

            var content = possibleContent.Result;
            Logger.Verbose($"(dirPath'{directoryPath}', dirId='{directoryId}') contains '{content.Count}' files:{Environment.NewLine}{string.Join(Environment.NewLine, content.Select(f => f.Render()))}");

            if (contentFilter != null)
            {
                var isIncludeFilter = filterKind == FilterKind.Include;
                var filteredContent = content.Where(file => contentFilter.IsMatch(file.FileName) == isIncludeFilter).ToList();
                Logger.Verbose("[dirId='{0}'] Filter ('{1}', {4}) excluded {2} file(s) out of {3}", directoryId, contentFilter, content.Count - filteredContent.Count, content.Count, filterKind);
                content = filteredContent;
            }

            return content;
        }

        private async Task<Possible<List<string>>> ParseManifestFilesAsync(List<SealedDirectoryFile> manifestFiles)
        {
            var parseManifestTasks = Enumerable
                .Range(0, manifestFiles.Count)
                .Select(i => m_config.ParserExeLocation != null
                    ? ParseManifestFileUsingExtenalParserAsync(manifestFiles[i].FileName)
                    // internal parsing is a temporary thing - it will be removed once an external parser is ready
                    : ParseManifestFileUsingInternaParser(manifestFiles[i].FileName))
                .ToArray();

            var possibleParseManifestResults = await TaskUtilities.SafeWhenAll(parseManifestTasks);
            if (possibleParseManifestResults.Any(res => !res.Succeeded))
            {
                return new Failure<string>(string.Join("; ", possibleParseManifestResults.Where(res => !res.Succeeded).Select(r => r.Failure.Describe())));
            }

            return possibleParseManifestResults.SelectMany(r => r.Result).ToList();
        }

        private async Task<Possible<List<string>>> ParseManifestFileUsingExtenalParserAsync(string manifestFilePath)
        {
            var process = CreateParserProcess(manifestFilePath);
            var retryCount = 0;
            try
            {
                while (true)
                {
                    Possible<List<string>> possibleResult;
                    using (m_counters.StartStopwatch(MaterializationDaemonCounter.ManifestParsingInnerDuration))
                    {
                        possibleResult = await launchExternalProcessAndParseOutputAsync(process);
                    }

                    if (possibleResult.Succeeded)
                    {
                        Logger.Verbose($"Manifest file ('{manifestFilePath}') content:{Environment.NewLine}{string.Join(Environment.NewLine, possibleResult.Result)}");
                        return possibleResult.Result;
                    }

                    if (retryCount < s_retryIntervals.Length)
                    {
                        m_counters.IncrementCounter(MaterializationDaemonCounter.ManifestParsingExternalParserRetriesCount);
                        m_counters.AddToCounter(MaterializationDaemonCounter.ManifestParsingExternalParserTotalRetryDelayDuration, (long)s_retryIntervals[retryCount].TotalMilliseconds);
                        Logger.Verbose($"Retrying an external parser due to an error. Retries left: {s_retryIntervals.Length - retryCount}. Error: {possibleResult.Failure.Describe()}.");
                        // wait before retrying
                        await Task.Delay(s_retryIntervals[retryCount]);

                        // need to recreate the process because AsyncProcessExecutor is disposing it
                        process = CreateParserProcess(manifestFilePath);
                    }
                    else
                    {
                        m_counters.IncrementCounter(MaterializationDaemonCounter.ManifestParsingExternalParserFailuresAfterRetriesCount);
                        Logger.Warning($"Failing an external parser because the number of retries were exhausted. The latest error: {possibleResult.Failure.Describe()}.");
                        return possibleResult.Failure;
                    }

                    retryCount++;
                }
            }
            catch (Exception e)
            {
                return new Failure<string>($"{describeProcess(process)} {e.DemystifyToString()}");
            }

            static async Task<Possible<List<string>>> launchExternalProcessAndParseOutputAsync(Process process)
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
                            return new Failure<string>($"{describeProcess(process)} Process failed with an exit code {processExecutor.Process.ExitCode}{stdOut}{stdErr}");
                        }

                        if (result.Count == 0)
                        {
                            return new Failure<string>($"{describeProcess(process)} Parser exited cleanly, but no output was written.");
                        }

                        if (!int.TryParse(result[0], out var expectedCount))
                        {
                            var stdOut = $"{Environment.NewLine}{string.Join(Environment.NewLine, result)}";
                            return new Failure<string>($"{describeProcess(process)} Failed to parse tool output: {stdOut}");
                        }

                        if (expectedCount != result.Count - 1)
                        {
                            var stdOut = $"{Environment.NewLine}{string.Join(Environment.NewLine, result)}";
                            return new Failure<string>($"{describeProcess(process)} Output line count does not match the expected count: {stdOut}");
                        }

                        result.RemoveAt(0);
                        return result;
                    }
                }
            }

            static string describeProcess(Process process)
            {
                return $"[Parser ('{process.StartInfo.FileName} {process.StartInfo.Arguments}')]";
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

        private Task<Possible<List<string>>> ParseManifestFileUsingInternaParser(string manifestFilePath)
        {
            try
            {
                var parser = new XmlManifestParser(manifestFilePath, m_macros);
                var files = parser.ExtractFiles();
                Logger.Verbose($"Manifest file ('{manifestFilePath}') content:{Environment.NewLine}{string.Join(Environment.NewLine, files)}");

                return Task.FromResult<Possible<List<string>>>(files);
            }
            catch (Exception e)
            {
                return Task.FromResult<Possible<List<string>>>(new Failure<string>(e.DemystifyToString()));
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

        private Possible<FilterKind[]> ParseFilterKinds(string[] filterKinds)
        {
            var res = new FilterKind[filterKinds.Length];
            for (int i = 0; i < filterKinds.Length; i++)
            {
                if (!Enum.TryParse(filterKinds[i], out FilterKind parsedValue))
                {
                    return new Failure<string>(I($"Failed to convert '{filterKinds[i]}' into a FilterKind value."));
                }

                res[i] = parsedValue;
            }

            return res;
        }

        private async Task ReportStatisticsAsync()
        {
            var stats = m_counters.AsStatistics("MaterializationDaemon");
            stats.AddRange(GetDaemonStats("MaterializationDaemon"));
            m_logger.Info($"Statistics:{string.Join(string.Empty, stats.Select(s => $"{Environment.NewLine}{s.Key}={s.Value}"))}");

            // report stats to BuildXL
            if (ApiClient != null)
            {
                var possiblyReported = await ApiClient.ReportStatistics(stats);
                if (possiblyReported.Succeeded && possiblyReported.Result)
                {
                    m_logger.Info("Statistics successfully reported to BuildXL.");
                }
                else
                {
                    var errorDescription = possiblyReported.Succeeded ? string.Empty : possiblyReported.Failure.Describe();
                    m_logger.Warning("Reporting stats to BuildXL failed. " + errorDescription);
                }
            }
            
        }

        private enum FilterKind : byte
        {
            Include,

            Exclude
        }

        private enum MaterializationDaemonCounter
        {
            [CounterType(CounterType.Stopwatch)]
            GetSealedDirectoryContentDuration,

            MaterializeDirectoriesRequestCount,

            MaterializeDirectoriesFilesToMaterialize,

            [CounterType(CounterType.Stopwatch)]
            MaterializeDirectoriesOuterMaterializationDuration,

            MaterializeDirectoriesMaterializationQueueDuration,

            RegisterManifestRequestCount,

            [CounterType(CounterType.Stopwatch)]
            RegisterManifestFileMaterializationDuration,

            RegisterManifestFileMaterializationQueueDuration,

            [CounterType(CounterType.Stopwatch)]
            RegisterManifestReferencedFileMaterializationDuration,

            RegisterManifestReferencedFileMaterializationQueueDuration,

            [CounterType(CounterType.Stopwatch)]
            ManifestParsingOuterDuration,

            [CounterType(CounterType.Stopwatch)]
            ManifestParsingInnerDuration,

            ManifestParsingTotalFiles,

            ManifestParsingReferencedTotalFiles,

            ManifestParsingExternalParserRetriesCount,

            ManifestParsingExternalParserFailuresAfterRetriesCount,

            ManifestParsingExternalParserTotalRetryDelayDuration,
        }
    }
}
