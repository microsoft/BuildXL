// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Distribution;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Engine.Recovery;
using BuildXL.Engine.Tracing;
using BuildXL.Engine.Visualization;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Ide.Generator;
using BuildXL.Native.IO;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Filter;
using BuildXL.Scheduler.Graph;
using BuildXL.Storage;
using BuildXL.Storage.ChangeJournalService;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Scheduler.ExecutionSampler;
using static BuildXL.Utilities.BuildParameters;
using static BuildXL.Utilities.FormattableStringEx;
using IOneBuildModuleConfiguration = BuildXL.Utilities.Configuration.IModuleConfiguration;
using Logger = BuildXL.Engine.Tracing.Logger;

namespace BuildXL.Engine
{
    /// <summary>
    /// BuildXL dependency evaluation engine.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public sealed partial class BuildXLEngine
    {
        /// <summary>
        /// Class that contains hooks for test to inspect the internal state
        /// while not having to hold on to the state after it is needed in regular executions
        /// </summary>
        internal EngineTestHooksData TestHooks { get; set; }

        /// <summary>
        /// Manages interactions with workers/coordinator for distributed builds.
        /// Exists only when engine runs in master mode.
        /// </summary>
        private readonly MasterService m_masterService;

        /// <summary>
        /// The worker service - exists only when the engine runs in worker mode.
        /// </summary>
        private readonly WorkerService m_workerService;

        /// <summary>
        /// Common representation of current distribution service
        /// </summary>
        private readonly IDistributionService m_distributionService;

        /// <summary>
        /// This holds the state that the engine needs to hand down to the components it hosts.
        /// </summary>
        public EngineContext Context;

        /// <summary>
        /// BuildXLEngine configuration
        /// </summary>
        /// <remarks>
        /// This object is not readonly because it is reset when we load a cached graph.
        /// Then we have a better new PathTable loaded from disk, so the paths in the configuration are moot then.
        /// When we reload the new configuration, the cache is responsible to set the right path.
        /// </remarks>
        public IConfiguration Configuration { get; private set; }

        /// <summary>
        /// The startup configuration
        /// </summary>
        /// <remarks>
        /// Only used for:
        ///     SnapshotCollector
        ///     Graph Caching fingerprinting logic
        ///     Pip Filtering
        /// </remarks>
        private ICommandLineConfiguration m_initialCommandLineConfiguration;

        /// <summary>
        /// The factory used to create an <see cref="IFrontEndController"/>.
        /// </summary>
        /// <remarks>
        /// Only used for graph patching: when <see cref="EngineContext"/> is reloaded from disk,
        /// the old one (used to originally create a <see cref="IFrontEndController"/>) is invalidated,
        /// so a new <see cref="IFrontEndController"/> must be created (using this factory) for the
        /// newly reloaded engine context.
        /// </remarks>
        private readonly IFrontEndControllerFactory m_frontEndControllerFactory;

        /// <summary>
        /// The FrontEnd controller
        /// </summary>
        internal IFrontEndController FrontEndController { get; private set; }

        /// <summary>
        /// FileContentTable used for retrieving file hash information
        /// </summary>
        /// <remarks>
        /// Starts loading asynchronously in Run() and will synchronously wait for loading to complete on first access.
        /// </remarks>
        private FileContentTable FileContentTable
        {
            get
            {
                if (m_fileContentTask != null)
                {
                    m_fileContentTable = m_fileContentTask.Result;
                    m_fileContentTask = null;
                }

                return m_fileContentTable;
            }
        }

        private FileContentTable m_fileContentTable;
        private Task<FileContentTable> m_fileContentTask;

        private Task<bool> m_graphCacheContentCachePut;

        private Task m_executionLogGraphCopy;
        private Task m_previousInputFilesCopy;

        #region External inspectors to the engine

        /// <summary>
        /// The visualization information
        /// </summary>
        private EngineLiveVisualizationInformation m_visualization;

        /// <summary>
        /// The snapshot collector.
        /// </summary>
        /// <remarks>
        /// Only set when initialized for snapshot collection
        /// </remarks>
        private SnapshotCollector m_snapshotCollector;

        #endregion External inspectors to the engine

        /// <summary>
        /// UTC time of when the process started
        /// </summary>
        private readonly DateTime m_processStartTimeUtc;

        /// <summary>
        /// Path translator for logging.
        /// </summary>
        private readonly PathTranslator m_translator;

        /// <summary>
        /// Root translator to use to translate paths for caching
        /// </summary>
        private readonly RootTranslator m_rootTranslator;

        /// <summary>
        /// Directory translator.
        /// </summary>
        private readonly DirectoryTranslator m_directoryTranslator;

        /// <summary>
        /// Default cache config file name.
        /// </summary>
        internal const string DefaultCacheConfigFileName = "DefaultCacheConfig.json";

        /// <summary>
        /// Name of temp directory used for file move-deletes
        /// </summary>
        private const string MoveDeleteTempDirectoryName = "MoveDeletionTemp";

        /// <summary>
        /// Full string path of the temp directory used for file move-deletes
        /// </summary>
        private readonly string m_moveDeleteTempDirectory;

        /// <summary>
        /// TempCleaner responsible for cleaning registered directories or files in the background.
        /// This is owned by the outermost layer that calls <see cref="FileUtilities.DeleteFile(string, bool, ITempDirectoryCleaner)"/>, the engine.
        /// </summary>
        private TempCleaner m_tempCleaner;

        /// <summary>
        /// Performance info used for logging at the app level
        /// </summary>
        private readonly EnginePerformanceInfo m_enginePerformanceInfo = new EnginePerformanceInfo();

        /// <summary>
        /// TrackingEventListener for failing the build when FileAccessErrors are not breaking the build.
        /// </summary>
        private readonly TrackingEventListener m_trackingEventListener;

        /// <summary>
        /// Whether to remember all changed inputs in <see cref="InputTracker.ChangedFiles"/>
        /// (used in <see cref="CheckIfAvailableInputsToGraphMatchPreviousRun"/>).
        /// </summary>
        private readonly bool m_rememberAllChangedTrackedInputs;

        /// <summary>
        /// Commit id of from build info.
        /// </summary>
        /// <remarks>
        /// This is only populate on official builds, on developer builds this null
        /// </remarks>
        [CanBeNull]
        private readonly string m_commitId;

        /// <summary>
        /// The version from build info.
        /// </summary>
        /// <remarks>
        /// This is only populate on official builds, on developer builds this null
        /// </remarks>
        [CanBeNull]
        private readonly string m_buildVersion;

        private AbsolutePath? m_workerSymlinkDefinitionFile = null;

        private bool IsDistributedMaster => Configuration.Distribution.BuildRole == DistributedBuildRoles.Master;

        private bool IsDistributedWorker => Configuration.Distribution.BuildRole == DistributedBuildRoles.Worker;

        /// <summary>
        /// Private constructor. Please use BuildXLEngine.Create
        /// </summary>
        private BuildXLEngine(
            LoggingContext loggingContext,
            EngineContext context,
            IConfiguration configuration,
            ICommandLineConfiguration initialConfig, // some configuration options are still only on the initialConfig. When cleaned up this argument and member should be removed.
            IFrontEndControllerFactory frontEndControllerFactory,
            IFrontEndController frontEndController,
            PerformanceCollector collector,
            DateTime? processStartTimeUtc,
            TrackingEventListener trackingEventListener,
            bool rememberAllChangedTrackedInputs,
            [CanBeNull] string commitId,
            [CanBeNull] string buildVersion)
        {
            Contract.Requires(context != null);
            Contract.Requires(configuration != null);
            Contract.Requires(initialConfig != null);

            if (configuration.Distribution.IsGrpcEnabled)
            {
                bool grpcHandlerInliningEnabled = GrpcSettings.HandlerInliningEnabled;

#if FEATURE_CORECLR
                // Handler inlining causing deadlock on the mac platform.
                grpcHandlerInliningEnabled = false;
#endif

                GrpcEnvironment.InitializeIfNeeded(GrpcSettings.ThreadPoolSize, grpcHandlerInliningEnabled);

                Logger.Log.GrpcSettings(loggingContext, GrpcSettings.ThreadPoolSize, grpcHandlerInliningEnabled, (int)GrpcSettings.CallTimeout.TotalMinutes, (int)GrpcSettings.InactiveTimeout.TotalMinutes);
            }

            Context = context;
            Configuration = configuration;

            m_initialCommandLineConfiguration = initialConfig;
            m_frontEndControllerFactory = frontEndControllerFactory;
            FrontEndController = frontEndController;
            m_processStartTimeUtc = processStartTimeUtc ?? DateTime.UtcNow;
            m_trackingEventListener = trackingEventListener;
            m_rememberAllChangedTrackedInputs = rememberAllChangedTrackedInputs;

            // Activity id is unique per the CB session. However, this is not enough as we invoke more than one BuildXL per machine per CB session due to the Office workflow.
            // That's why, we also concat the environment to the id.
            var distributedBuildId = I($"{Configuration.Logging.RelatedActivityId}-{Configuration.Logging.Environment}");
            if (IsDistributedMaster)
            {
                m_masterService = new MasterService(Configuration.Distribution, loggingContext, distributedBuildId);
                m_distributionService = m_masterService;
            }
            else if (IsDistributedWorker)
            {
                m_workerService = new WorkerService(
                    loggingContext,
                    Configuration.Schedule.MaxProcesses,
                    Configuration.Distribution,
                    distributedBuildId);
                m_distributionService = m_workerService;
            }

            PathTranslator.CreateIfEnabled(configuration.Logging.SubstTarget, configuration.Logging.SubstSource, Context.PathTable, out m_translator);
            m_directoryTranslator = CreateDirectoryTranslator(Configuration);
            m_rootTranslator = new RootTranslator(m_directoryTranslator.GetReverseTranslator());

            m_collector = collector;
            m_commitId = commitId;
            m_buildVersion = buildVersion;

            // Designate a temp directory under ObjectDirectory for FileUtilities to move files to during deletion attempts
            m_moveDeleteTempDirectory = Path.Combine(configuration.Layout.ObjectDirectory.ToString(context.PathTable), MoveDeleteTempDirectoryName);
        }

        /// <summary>
        /// Constructs a new engine
        /// </summary>
        public static BuildXLEngine Create(
            LoggingContext loggingContext,
            EngineContext context,
            ICommandLineConfiguration initialCommandLineConfiguration,
            IFrontEndControllerFactory frontEndControllerFactory,
            PerformanceCollector collector = null,
            DateTime? processStartTimeUtc = null,
            TrackingEventListener trackingEventListener = null,
            bool rememberAllChangedTrackedInputs = false,
            string commitId = null,
            string buildVersion = null)
        {
            Contract.Requires(context != null);
            Contract.Requires(initialCommandLineConfiguration != null);
            Contract.Requires(initialCommandLineConfiguration.Layout != null);
            Contract.Requires(initialCommandLineConfiguration.Layout.PrimaryConfigFile.IsValid, "The caller is responsible for making sure the initial layout is properly configured by calling PopulateLoggingAndLayoutConfiguration on the config.");
            Contract.Requires(initialCommandLineConfiguration.Layout.BuildEngineDirectory.IsValid, "The caller is responsible for making sure the initial layout is properly configured by calling PopulateLoggingAndLayoutConfiguration on the config.");

            // Engine is responsible for front-end construction.
            // This helps to release all front-end related memory right after evaluation phase.
            var frontEndController = frontEndControllerFactory.Create(context.PathTable, context.SymbolTable);
            if (frontEndController == null)
            {
                return null;
            }

            // Use a copy of the provided configuration. The engine mutates the configuration and invalidates old copies
            // as a safety mechanism against using out of date references. An external consumer of the engine may want
            // to use the same config for multiple engine runs. So make a copy here to avoid invalidating the config
            // object that a consumer may want to use later
            var mutableInitialConfig = new CommandLineConfiguration(initialCommandLineConfiguration);

            if (mutableInitialConfig.InCloudBuild())
            {
                mutableInitialConfig.Startup.EnsurePropertiesWhenRunInCloudBuild();
                ApplyTemporaryHackWhenRunInCloudBuild(context, mutableInitialConfig);
            }

            if (mutableInitialConfig.Layout.RedirectedUserProfileJunctionRoot.IsValid && !OperatingSystemHelper.IsUnixOS)
            {
                if (!RedirectUserProfileDirectory(
                    mutableInitialConfig.Layout.RedirectedUserProfileJunctionRoot,
                    mutableInitialConfig.Engine.DirectoriesToTranslate,
                    mutableInitialConfig.Startup.Properties,
                    SpecialFolderUtilities.InitRedirectedUserProfilePaths,
                    context.PathTable,
                    loggingContext))
                {
                    Contract.Assume(loggingContext.ErrorWasLogged, "Failed to redirect user profile, but no error was logged.");
                    return null;
                }
            }

            initialCommandLineConfiguration = mutableInitialConfig;

            var frontEndContext = context.ToFrontEndContext(loggingContext);
            frontEndController.InitializeHost(frontEndContext, initialCommandLineConfiguration);

            ConfigurationImpl configuration;
            using (PerformanceMeasurement.StartWithoutStatistic(
                loggingContext,
                Logger.Log.StartParseConfig,
                Logger.Log.EndParseConfig))
            {
                var parsedConfiguration = frontEndController.ParseConfig(initialCommandLineConfiguration);
                if (parsedConfiguration == null)
                {
                    return null;
                }

                configuration = new ConfigurationImpl(parsedConfiguration);
            }

            PopulateFileSystemCapabilities(configuration, initialCommandLineConfiguration, context.PathTable, loggingContext);

            // No need to call PopulateLoggingAndLayoutConfiguration, the caller is responsible for calling this.
            if (!PopulateAndValidateConfiguration(configuration, initialCommandLineConfiguration, context.PathTable, loggingContext))
            {
                Contract.Assume(loggingContext.ErrorWasLogged, "Engine configuration failed the validation, but no error was logged.");
                return null;
            }

            return new BuildXLEngine(
                loggingContext,
                context,
                configuration,
                initialCommandLineConfiguration,
                frontEndControllerFactory,
                frontEndController,
                collector,
                processStartTimeUtc,
                trackingEventListener,
                rememberAllChangedTrackedInputs,
                commitId,
                buildVersion);
        }

        /// <summary>
        /// Temporary hack when BuildXL run in CloudBuild.
        /// </summary>
        /// <remarks>
        /// Once CloudBuild people fix the issue, then this hack can be removed.
        /// </remarks>
        private static void ApplyTemporaryHackWhenRunInCloudBuild(EngineContext context, CommandLineConfiguration commandLineConfiguration)
        {
            Contract.Requires(commandLineConfiguration != null);
            Contract.Requires(commandLineConfiguration.InCloudBuild());

            // Replace directories in environment containing CloudBuild session id with the one without.
            // The environment variables that have been identified so far are the following:
            // - BUILDXL_DROP_CONFIG
            // - __CLOUDBUILD_AUTH_HELPER_CONFIG__
            // - __CREDENTIAL_PROVIDER_LOG_DIR
            var regex = new Regex(@"d:\\dbs\\sh\\(?<enlistmentShortName>\w+)\\[0-9]+_[0-9]+(_[0-9]+)?\\", RegexOptions.IgnoreCase);
            const string Replacement = @"K:\dbs\sh\${enlistmentShortName}\b\";

            bool checkedHasDirectoryTranslation = false;

            foreach (DictionaryEntry envVar in Environment.GetEnvironmentVariables())
            {
                var key = (string)envVar.Key;
                string value = (string)envVar.Value;

                if (DisallowedTempVariables.Contains(key))
                {
                    // Skip for disallowed temp variables.
                    continue;
                }

                if (commandLineConfiguration.Startup.Properties.ContainsKey(key))
                {
                    // Skip if it is explicitly specified by users.
                    continue;
                }

                if (string.Equals(key, "SystemRoot", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "windir", StringComparison.OrdinalIgnoreCase))
                {
                    // Normalize %SystemRoot% and %WinDir%.
                    commandLineConfiguration.Startup.Properties.Add(key.ToUpperInvariant(), value.ToUpperInvariant());
                    continue;
                }

                if (!string.IsNullOrEmpty(value))
                {
                    var replacedValue = regex.Replace(value, Replacement);
                    if (!checkedHasDirectoryTranslation && replacedValue != value)
                    {
                        var sessionDirMatch = regex.Matches(value)[0];
                        var sessionDir = sessionDirMatch.Value;
                        var fixedSsdSessionDir = sessionDirMatch.Result(Replacement);

                        var sessionDirPath = AbsolutePath.Create(context.PathTable, sessionDir);
                        var fixedSsdSessionDirPath = AbsolutePath.Create(context.PathTable, fixedSsdSessionDir);

                        if (!Directory.Exists(fixedSsdSessionDir))
                        {
                            return;
                        }

                        foreach (var directoryTranslation in commandLineConfiguration.Engine.DirectoriesToTranslate)
                        {
                            // Check for directory translation between the paths.
                            if ((sessionDirPath == directoryTranslation.FromPath && fixedSsdSessionDirPath == directoryTranslation.ToPath) ||
                                (fixedSsdSessionDirPath == directoryTranslation.FromPath && sessionDirPath == directoryTranslation.ToPath))
                            {
                                checkedHasDirectoryTranslation = true;
                            }
                        }

                        if (!checkedHasDirectoryTranslation)
                        {
                            // todo should this be 'continue' (bug 1386527)
                            return;
                        }
                    }
                    commandLineConfiguration.Startup.Properties.Add(key, replacedValue);
                }
            }
        }

        private static AbsolutePath AppendNoIndexSuffixToLayoutDirectoryIfNeeded(PathTable pathTable, AbsolutePath directory, ILayoutConfiguration layout, bool inTestMode)
        {
            if (OperatingSystemHelper.IsUnixOS && !inTestMode)
            {
                if (PathAtom.TryCreate(pathTable.StringTable, Strings.Layout_DefaultNoIndexSuffix, out var suffix))
                {
                    // If we are running on macOS, we add the Layout_DefaultNoIndexSuffix to our artifact directories so Spotlight does not index their contents.
                    // This only happens if the user has not specified custom directories on the command line, in that case, we only emit a warning.
                    // This helps reducing memory and cpu usage during very involved builds.
                    if (!directory.ToString(pathTable).EndsWith(Strings.Layout_DefaultNoIndexSuffix))
                    {
                        return directory.Concat(pathTable, suffix);
                    }
                }
            }

            return directory;
        }

        /// <summary>
        /// Apps should call this method to ensure that the logging and layout parts of the config are properly populated.
        /// </summary>
        /// <remarks>
        /// When modifying the config object in this function and adding more implicit settings, please review this entire function and PopulateConfiguration
        /// To make sure that there is no foul interplay between feature A turning feature X on and feature B turning feature X off. I.e., what should happen when A and B are both enabled.
        /// </remarks>
        public static void PopulateLoggingAndLayoutConfiguration(CommandLineConfiguration mutableConfig, PathTable pathTable, string bxlExeLocation, bool inTestMode = false)
        {
            Contract.Requires(mutableConfig != null);

            var primaryConfigFile = mutableConfig.Startup.ConfigFile;
            var layout = mutableConfig.Layout;
            var logging = mutableConfig.Logging;

            // Configure layout inferred values.
            if (!layout.PrimaryConfigFile.IsValid)
            {
                layout.PrimaryConfigFile = primaryConfigFile;
            }

            if (!layout.SourceDirectory.IsValid)
            {
                layout.SourceDirectory = primaryConfigFile.GetParent(pathTable);
            }

            if (!layout.OutputDirectory.IsValid)
            {
                layout.OutputDirectory = layout.SourceDirectory.Combine(pathTable, Strings.Layout_DefaultOutputFolderName);
            }

            if (!layout.ObjectDirectory.IsValid)
            {
                var objectDirectory = layout.OutputDirectory.Combine(pathTable, Strings.Layout_DefaultObjectsFolderName);
                layout.ObjectDirectory = AppendNoIndexSuffixToLayoutDirectoryIfNeeded(pathTable, objectDirectory, layout, inTestMode);
            }

            if (!layout.RedirectedDirectory.IsValid)
            {
                // By default the root of all redirected directories (that is, for pips running in containers) is directly under object root
                // with a well-known name. This allows for easily identifying if a file is a redirected one.
                var redirectedDirectory = layout.ObjectDirectory.Combine(pathTable, Strings.Layout_DefaultRedirectedFolderName);
                layout.RedirectedDirectory = AppendNoIndexSuffixToLayoutDirectoryIfNeeded(pathTable, redirectedDirectory, layout, inTestMode);
            }

            if (!layout.FrontEndDirectory.IsValid)
            {
                var frontEndDirectory = layout.ObjectDirectory.GetParent(pathTable).Combine(pathTable, Strings.Layout_DefaultFrontEndFolderName);
                layout.FrontEndDirectory = AppendNoIndexSuffixToLayoutDirectoryIfNeeded(pathTable, frontEndDirectory, layout, inTestMode);
            }

            if (!layout.CacheDirectory.IsValid)
            {
                var cacheDirectory = layout.OutputDirectory.Combine(pathTable, Strings.Layout_DefaultCacheFolderName);
                layout.CacheDirectory = AppendNoIndexSuffixToLayoutDirectoryIfNeeded(pathTable, cacheDirectory, layout, inTestMode);
            }

            if (!layout.EngineCacheDirectory.IsValid)
            {
                var engineCacheDirectory = layout.CacheDirectory.Combine(pathTable, Strings.Layout_DefaultEngineCacheFolderName);
                layout.EngineCacheDirectory = AppendNoIndexSuffixToLayoutDirectoryIfNeeded(pathTable, engineCacheDirectory, layout, inTestMode);
            }

            if (!layout.BuildEngineDirectory.IsValid)
            {
                var assemblyLocation = bxlExeLocation ?? AssemblyHelper.GetAssemblyLocation(typeof(BuildXLEngine).GetTypeInfo().Assembly);
                layout.BuildEngineDirectory = AbsolutePath.Create(pathTable, assemblyLocation).GetParent(pathTable);
            }

            if (!layout.FileContentTableFile.IsValid)
            {
                layout.FileContentTableFile = layout.EngineCacheDirectory.Combine(pathTable, Strings.Layout_DefaultFileContentTableFileName);
            }

            if (!layout.SchedulerFileChangeTrackerFile.IsValid)
            {
                if (!mutableConfig.InCloudBuild())
                {
                    // Default outside of CloudBuild or when disabled
                    layout.SchedulerFileChangeTrackerFile = layout.EngineCacheDirectory.Combine(pathTable, Scheduler.Scheduler.DefaultSchedulerFileChangeTrackerFile);
                }
                else
                {
                    // Place the scheduler change tracker file next to the file content table in cloud build
                    layout.SchedulerFileChangeTrackerFile = layout.FileContentTableFile.GetParent(pathTable).Combine(pathTable, Scheduler.Scheduler.DefaultSchedulerFileChangeTrackerFile);
                }
            }

            if (!layout.IncrementalSchedulingStateFile.IsValid)
            {
                layout.IncrementalSchedulingStateFile = layout.EngineCacheDirectory.Combine(pathTable, Scheduler.Scheduler.DefaultIncrementalSchedulingStateFile);
            }

            if (!layout.FingerprintStoreDirectory.IsValid)
            {
                layout.FingerprintStoreDirectory = layout.EngineCacheDirectory.Combine(pathTable, Scheduler.Scheduler.FingerprintStoreDirectory);
            }

            // Logging
            if (string.IsNullOrEmpty(logging.LogPrefix))
            {
                logging.LogPrefix = LogFileExtensions.DefaultLogPrefix;
            }

            if (!logging.LogsDirectory.IsValid)
            {
                logging.LogsDirectory = layout.OutputDirectory.Combine(pathTable, Strings.Layout_DefaultLogFolderName);
            }

            if (logging.LogsToRetain > 0)
            {
                string logsRootPath = logging.LogsDirectory.ToString(pathTable);
                string dateBasedLogName = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string uniqueLogName = dateBasedLogName;

                int idx = 1;
                while (Directory.Exists(Path.Combine(logsRootPath, uniqueLogName)))
                {
                    uniqueLogName = I($"{dateBasedLogName}.{idx}");
                    idx++;
                }

                logging.LogsDirectory = logging.LogsDirectory.Combine(pathTable, uniqueLogName);
            }

            logging.RedirectedLogsDirectory = layout.OutputDirectory.Combine(pathTable, Strings.Layout_DefaultJunctionNameTotLogFolder);

            mutableConfig.Sandbox.TimeoutDumpDirectory = logging.LogsDirectory.Combine(pathTable, "TimeoutDumps");

            logging.Log = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.Log);
            logging.ErrorLog = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.Errors);
            logging.WarningLog = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.Warnings);

            if (logging.LogStats)
            {
                logging.StatsLog = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.Stats);
            }

            if (logging.LogStatus)
            {
                logging.StatusLog = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.Status);
            }

            if (logging.LogExecution)
            {
                logging.ExecutionLog = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.Execution);
            }

            if (!logging.CacheMissLog.IsValid)
            {
                logging.CacheMissLog = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.CacheMissLog);
            }

            if (!logging.PipOutputLog.IsValid)
            {
                logging.PipOutputLog = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.PipOutputLog);
            }

            if (!logging.DevLog.IsValid)
            {
                logging.DevLog = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.DevLog);
            }

            if (!logging.RpcLog.IsValid)
            {
                logging.RpcLog = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.RpcLog);
            }

            if (!mutableConfig.Cache.CacheLogFilePath.IsValid)
            {
                mutableConfig.Cache.CacheLogFilePath = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix + LogFileExtensions.Cache);
            }

            if (!logging.EngineCacheLogDirectory.IsValid)
            {
                logging.EngineCacheLogDirectory = logging.LogsDirectory.Combine(pathTable, logging.LogPrefix);
                logging.EngineCacheCorruptFilesLogDirectory = logging.EngineCacheLogDirectory.Combine(pathTable, EngineSerializer.CorruptFilesLogLocation);
            }

            if (!logging.FingerprintsLogDirectory.IsValid)
            {
                logging.FingerprintsLogDirectory = logging.LogsDirectory.Combine(pathTable, LogFileExtensions.FingerprintsLogDirectory);
            }

            if (!logging.ExecutionFingerprintStoreLogDirectory.IsValid)
            {
                logging.ExecutionFingerprintStoreLogDirectory = logging.FingerprintsLogDirectory.Combine(pathTable, Scheduler.Scheduler.FingerprintStoreDirectory);
            }

            if (!logging.CacheLookupFingerprintStoreLogDirectory.IsValid)
            {
                logging.CacheLookupFingerprintStoreLogDirectory = logging.FingerprintsLogDirectory.Combine(pathTable, Scheduler.Scheduler.FingerprintStoreDirectory + LogFileExtensions.CacheLookupFingerprintStore);
            }

            if (mutableConfig.Cache.HistoricMetadataCache == true && !logging.HistoricMetadataCacheLogDirectory.IsValid)
            {
                logging.HistoricMetadataCacheLogDirectory = logging.EngineCacheLogDirectory.Combine(pathTable, EngineSerializer.HistoricMetadataCacheLocation);
            }
        }

        /// <summary>
        /// Populates file system capability.
        /// </summary>
        public static void PopulateFileSystemCapabilities(
            ConfigurationImpl mutableConfig, 
            ICommandLineConfiguration initialCommandLineConfiguration, 
            PathTable pathTable, 
            LoggingContext loggingContext)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                string mainConfigFile = initialCommandLineConfiguration.Startup.ConfigFile.ToString(pathTable);

                if (FileUtilities.FileExistsNoFollow(mainConfigFile))
                {
                    // This method is also called from tests, but they may have non-existent config file.
                    using (var configFileStream = FileUtilities.CreateFileStream(
                        mainConfigFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete))
                    {
                        FileUtilities.IsPreciseFileVersionSupportedByEnlistmentVolume = VersionedFileIdentity.HasPreciseFileVersion(configFileStream.SafeFileHandle);
                        FileUtilities.IsCopyOnWriteSupportedByEnlistmentVolume = FileUtilities.CheckIfVolumeSupportsCopyOnWriteByHandle(configFileStream.SafeFileHandle);
                    }
                }
            }
        }

        /// <summary>
        /// This method takes the configuration as provided by the user in the config file and the commandline
        /// and applies custom defaulting rules and inference for omitted information.
        /// </summary>
        /// <remarks>
        /// When modifying the config object in this funciton and adding more implicit settings, please review this entire function and PopulateLoggingAndLayoutConfiguration
        /// To make sure that there is no foul interplay between feature A turning feature X on and feature B turning feature X off. I.e., what should happen when A and B are both enabled.
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static bool PopulateAndValidateConfiguration(
            ConfigurationImpl mutableConfig,
            ICommandLineConfiguration initialCommandLineConfiguration,
            PathTable pathTable,
            LoggingContext loggingContext)
        {
            bool success = true;

            foreach (var property in initialCommandLineConfiguration.Startup.Properties)
            {
                EngineEnvironmentSettings.SetVariable(property.Key, property.Value);
            }

            if (mutableConfig.Export.SnapshotFile.IsValid && mutableConfig.Export.SnapshotMode != SnapshotMode.None)
            {
                // Note: the /CleanOnly also overrides the Phase. In this case it is safe. An evaluation snapshot 'can' work with the 'schedule' phase in case both are set.
                mutableConfig.Engine.Phase = mutableConfig.Export.SnapshotMode == SnapshotMode.Full
                    ? EnginePhases.Schedule
                    : EnginePhases.Evaluate;

                // TODO: Snapshot mode does not work when loading cached graph. Can input tracker be used to capture/load the same information as
                // the snapshot collector so snapshot works with graph caching.
                mutableConfig.Cache.CacheGraph = false;
            }

            // The /cleanonly option should override the phase to only schedule and not perform execution
            if (mutableConfig.Engine.CleanOnly)
            {
                // Note: the snapshot option also sets the phase. In this case it is safe. An evaluation snapshot 'can' work with the 'schedule' phase in case both are set.
                mutableConfig.Engine.Phase = EnginePhases.Schedule;
            }

            // Distribution overrides
            if (mutableConfig.Distribution.BuildRole == DistributedBuildRoles.Master)
            {
                if (!mutableConfig.Distribution.BuildWorkers.Any())
                {
                    // Disable the distribution if no remote worker is given.
                    mutableConfig.Distribution.BuildRole = DistributedBuildRoles.None;
                }
                else
                {
                    // Force graph caching because the master needs to communicate it to the worker.
                    mutableConfig.Cache.CacheGraph = true;
                }
            }

            if (mutableConfig.Distribution.BuildRole != DistributedBuildRoles.Master)
            {
                // No additional choose worker threads needed in single machine builds or workers
                mutableConfig.Schedule.MaxChooseWorkerCpu = 1;
                mutableConfig.Schedule.MaxChooseWorkerCacheLookup = 1;
            }

            if (mutableConfig.Distribution.BuildRole == DistributedBuildRoles.Worker)
            {
                // No reason for the worker to interact with HistoricPerformanceInfo. Scheduling decisions are handled by the master
                mutableConfig.Schedule.UseHistoricalPerformanceInfo = false;
            }

            // Caching overrides
            if (mutableConfig.Cache.CachedGraphLastBuildLoad)
            {
                mutableConfig.Cache.CachedGraphPathToLoad = mutableConfig.Layout.EngineCacheDirectory;
                mutableConfig.Cache.CachedGraphLastBuildLoad = false;
            }

            // Cache config file path.
            if (!mutableConfig.Cache.CacheConfigFile.IsValid)
            {
                // Use default cache config file if nothing is specified.
                Assembly executingAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var executingAssemblyDirectory = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(executingAssembly));
                Contract.Assert(!string.IsNullOrEmpty(executingAssemblyDirectory));

                var defaultCacheConfigFile = AbsolutePath.Create(pathTable, Path.Combine(executingAssemblyDirectory, DefaultCacheConfigFileName));
                mutableConfig.Cache.CacheConfigFile = defaultCacheConfigFile;
            }

            // There is funny handling in the configuration for these two settings in conjuction with whitelist settings:
            //  * Turning off UnexpectedFileAccessesAreErrors (this code), or
            //  * Declaring a whitelist in config.
            // (story 169157) Tracks cleaning this up.
            mutableConfig.Sandbox.FailUnexpectedFileAccesses = mutableConfig.Sandbox.UnsafeSandboxConfiguration.UnexpectedFileAccessesAreErrors;

            // New semantics of /unsafe_DisableDetours --> fully disables sandboxing and runs processes using the plain .NET Process class.
            // This effectively means that MonitorFileAccesses should be disabled.
            if (mutableConfig.Sandbox.UnsafeSandboxConfiguration.DisableDetours())
            {
                mutableConfig.Sandbox.UnsafeSandboxConfigurationMutable.MonitorFileAccesses = false;
            }

            foreach (var variable in mutableConfig.AllowedEnvironmentVariables)
            {
                if (DisallowedTempVariables.Contains(variable))
                {
                    Logger.Log.EnvironmentValueForTempDisallowed(
                        loggingContext,
                        mutableConfig.Layout.PrimaryConfigFile.ToString(pathTable),
                        0,
                        0,
                        variable);
                    success = false;
                }
            }

            mutableConfig.Location = new LocationData(mutableConfig.Layout.PrimaryConfigFile, 0, 0);

            // Validate the global module configuration from the config file
            success &= ValidateModuleConfig(mutableConfig, pathTable, loggingContext, mutableConfig.Sandbox);

            foreach (var moduleConfiguration in mutableConfig.ModulePolicies.Values)
            {
                // Validate the module specific configurations
                success &= ValidateModuleConfig(moduleConfiguration, pathTable, loggingContext, mutableConfig.Sandbox);
            }

            // Directory translation.
            success &= ValidateSubstAndDirectoryTranslation(mutableConfig, pathTable, loggingContext);

            // Configure Ide Generation if it is enabled
            if (mutableConfig.Ide.IsEnabled)
            {
                IdeGenerator.Configure(mutableConfig, initialCommandLineConfiguration.Startup, pathTable);
            }

            var frontEndConfig = mutableConfig.FrontEnd;

            // Incremental frontend is turned off for office.
            if (frontEndConfig.UseLegacyOfficeLogic == true)
            {
                frontEndConfig.EnableIncrementalFrontEnd = false;
            }

            // Reusing ast and public facade implies that the binding fingerprint is saved
            // It also implies that we need to partially reload the engine state, since consistent
            // tables across builds are needed
            if (frontEndConfig.UseSpecPublicFacadeAndAstWhenAvailable())
            {
                frontEndConfig.ConstructAndSaveBindingFingerprint = true;
                frontEndConfig.ReloadPartialEngineStateWhenPossible = true;
            }

            if (mutableConfig.Schedule.ForceSkipDependencies != ForceSkipDependenciesMode.Disabled)
            {
                // Lazy materialization is not compatible with force skip dependencies
                mutableConfig.Schedule.EnableLazyOutputMaterialization = false;
            }

            if (mutableConfig.Schedule.AdaptiveIO)
            {
                Contract.Assert(mutableConfig.Logging.LogCounters, "AdaptiveIO requires the logCounters flag");

                // If the adaptive IO is enabled and the user does not pass a custom maxIO value, then use the number of processors as the IO limit.
                mutableConfig.Schedule.MaxIO = Environment.ProcessorCount;

                // If the adaptive IO is enabled and the user does not pass a custom statusFrequencyMs, then use 1000ms as a statusFrequency.
                // In the interval of 1000ms, both status messages will be printed on console and the maximum limit for the IO will be adjusted.
                if (mutableConfig.Logging.StatusFrequencyMs == 0)
                {
                    mutableConfig.Logging.StatusFrequencyMs = 1000;
                }
            }

            if (!mutableConfig.Logging.FailPipOnFileAccessError)
            {
                mutableConfig.Sandbox.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = false;
            }

            // Turn off incremental scheduling when incompatible features are enabled.
            if (mutableConfig.Schedule.IncrementalScheduling)
            {
                if (!mutableConfig.Cache.Incremental)
                {
                    // If incremental is turned off, incremental scheduling should be too. Don't bother logging.
                    mutableConfig.Schedule.IncrementalScheduling = false;
                }

                if (!mutableConfig.Engine.ScanChangeJournal)
                {
                    // If scan change journal is turned off, incremental scheduling should be too. Don't bother logging.
                    mutableConfig.Schedule.IncrementalScheduling = false;
                }

                if (mutableConfig.Cache.FileChangeTrackingExclusionRoots.Count != 0)
                {
                    mutableConfig.Schedule.IncrementalScheduling = false;
                    Logger.Log.ConfigIncompatibleIncrementalSchedulingDisabled(loggingContext, "/fileChangeTrackingExclusionRoot");
                }

                if (mutableConfig.Cache.FileChangeTrackingInclusionRoots.Count != 0)
                {
                    mutableConfig.Schedule.IncrementalScheduling = false;
                    Logger.Log.ConfigIncompatibleIncrementalSchedulingDisabled(loggingContext, "/fileChangeTrackingInclusionRoot");
                }

                if (mutableConfig.Schedule.SkipHashSourceFile)
                {
                    mutableConfig.Schedule.IncrementalScheduling = false;
                    Logger.Log.ConfigIncompatibleIncrementalSchedulingDisabled(loggingContext, "/skipHashSourceFile");
                }

                // If incremental scheduling is still on, then we need to compute the static fingerprints.
                if (mutableConfig.Schedule.IncrementalScheduling)
                {
                    mutableConfig.Schedule.ComputePipStaticFingerprints = true;
                }
            }

            // Environment-specific defaults/behaviors
            mutableConfig.Schedule.EnvironmentFingerprint = I($"RunningEnvironment:{mutableConfig.Logging.Environment}|InCloudBuild:{mutableConfig.InCloudBuild()}");

            // Distributed build overrides (including all CB builds because one worker CB builds are configured as distributed)
            if (mutableConfig.Distribution.BuildRole != DistributedBuildRoles.None)
            {
                mutableConfig.Schedule.ScheduleMetaPips = false;

                if (!mutableConfig.Schedule.StoreOutputsToCache)
                {
                    mutableConfig.Schedule.StoreOutputsToCache = true;
                    Logger.Log.ConfigIncompatibleOptionWithDistributedBuildWarn(
                        loggingContext,
                        "/storeOutputsToCache",
                        mutableConfig.Schedule.StoreOutputsToCache.ToString(CultureInfo.InvariantCulture),
                        true.ToString(CultureInfo.InvariantCulture));
                }

                if (mutableConfig.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs != PreserveOutputsMode.Disabled)
                {
                    mutableConfig.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Disabled;
                    Logger.Log.ConfigIncompatibleOptionWithDistributedBuildWarn(
                        loggingContext,
                        "/unsafe_PreserveOutputs",
                        mutableConfig.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs.ToString(),
                        PreserveOutputsMode.Disabled.ToString());
                }
            }

            // CloudBuild overrides
            if (mutableConfig.InCloudBuild())
            {
                // Enable fail fast for null reference exceptions caught by
                // ExceptionUtilities.IsUnexpectedException
                EngineEnvironmentSettings.FailFastOnNullReferenceException.Value = true;

                mutableConfig.Engine.ScanChangeJournal = false;
                mutableConfig.Schedule.IncrementalScheduling = false;

                // Disable viewer
                mutableConfig.Viewer = ViewerMode.Disable;

                // Enable historic ram based throttling in CloudBuild by default if it is not explicitly disabled.
                mutableConfig.Schedule.UseHistoricalRamUsageInfo = initialCommandLineConfiguration.Schedule.UseHistoricalRamUsageInfo ?? true;

                mutableConfig.Cache.FileContentTableEntryTimeToLive = mutableConfig.Cache.FileContentTableEntryTimeToLive ?? 100;

                // Minimize output materialization in cloudbuild
                mutableConfig.Schedule.EnableLazyWriteFileMaterialization = true;
                mutableConfig.Schedule.WriteIpcOutput = false;
                mutableConfig.Logging.ReplayWarnings = false;

                // Use compression for graph files to greatly reduce the size
                mutableConfig.Engine.CompressGraphFiles = true;

                // Ensure that historic perf data is retrieved from cache because engine cache
                // can be reused for multiple locations
                mutableConfig.Schedule.ForceUseEngineInfoFromCache = true;
                mutableConfig.Distribution.DistributeCacheLookups = true;
                mutableConfig.Cache.HistoricMetadataCache = initialCommandLineConfiguration.Cache.HistoricMetadataCache ?? true;
                mutableConfig.Schedule.ScheduleMetaPips = false;

                // Prefer to load data into memory
                mutableConfig.Cache.CacheMemoryUsage = MemoryUsageOption.Liberal;

                // In CloudBuild always place EngineCache under object directory
                mutableConfig.Layout.EngineCacheDirectory = mutableConfig.Layout.ObjectDirectory.Combine(pathTable, Strings.Layout_DefaultEngineCacheFolderName);

                // Add additional context to environment fingerprint
                foreach (var entry in mutableConfig.Logging.TraceInfo)
                {
                    if (entry.Key.Equals(TraceInfoExtensions.Branch, StringComparison.OrdinalIgnoreCase))
                    {
                        mutableConfig.Schedule.EnvironmentFingerprint += I($"|{TraceInfoExtensions.Branch}:{entry.Value}");
                    }
                    else if (entry.Key.Equals(TraceInfoExtensions.CloudBuildQueue, StringComparison.OrdinalIgnoreCase))
                    {
                        mutableConfig.Schedule.EnvironmentFingerprint += I($"|{TraceInfoExtensions.CloudBuildQueue}:{entry.Value}");
                    }
                }

                if (mutableConfig.Logging.CacheMissAnalysisOption.Mode == CacheMissMode.Local)
                {
                    // BuildXL should not use local fingerprintstore for cache miss analysis.
                    // Because you do not know how old is that fingerprintstore, the data is not really useful.
                    mutableConfig.Logging.CacheMissAnalysisOption.Mode = CacheMissMode.Disabled;
                }

                mutableConfig.Logging.StoreFingerprints = initialCommandLineConfiguration.Logging.StoreFingerprints ?? false;
                mutableConfig.Sandbox.RetryOnAzureWatsonExitCode = true;
            }
            else
            {
                mutableConfig.Logging.StoreFingerprints = initialCommandLineConfiguration.Logging.StoreFingerprints ?? true;
            }

            // Unless otherwise specified, distributed metabuilds in CloudBuild should replicate outputs to all machines
            // TODO: Remove this once reduced metabuild materialization is fully tested
            if (mutableConfig.InCloudBuild() &&
                mutableConfig.Distribution.ReplicateOutputsToWorkers == null &&
                mutableConfig.Logging.Environment == ExecutionEnvironment.OfficeMetaBuildLab &&
                mutableConfig.Distribution.BuildRole == DistributedBuildRoles.Master)
            {
                mutableConfig.Distribution.ReplicateOutputsToWorkers = true;
            }

            // If graph patching is requested, we need to reload the engine state when possible
            if (frontEndConfig.UseGraphPatching())
            {
                frontEndConfig.ReloadPartialEngineStateWhenPossible = true;
            }

            // If incremental is turned off, HistoricMetadataCache should also be turned off.
            if (!mutableConfig.Cache.Incremental)
            {
                mutableConfig.Cache.HistoricMetadataCache = false;
            }

            // If runtime cache miss analysis is enabled, the fingerprint store is required.
            if (mutableConfig.Logging.CacheMissAnalysisOption.Mode != CacheMissMode.Disabled)
            {
                mutableConfig.Logging.StoreFingerprints = true;
            }

            // EarlyWorkerRelease is only enabled for Office ProductBuild lab and OSG lab builds.
            if (mutableConfig.Logging.Environment != ExecutionEnvironment.OfficeProductBuildLab &&
                mutableConfig.Logging.Environment != ExecutionEnvironment.OsgLab)
            {
                mutableConfig.Schedule.EarlyWorkerRelease = false;
            }

            if (mutableConfig.Distribution.ReplicateOutputsToWorkers == true)
            {
                mutableConfig.Schedule.EarlyWorkerRelease = false;
            }

            return success;
        }

        internal static bool RedirectUserProfileDirectory(
            AbsolutePath root,
            List<TranslateDirectoryData> redirectedDirectories,
            Dictionary<string, string> properties,
            Action<IReadOnlyDictionary<string, string>> specialFolderInitializer,
            PathTable pathTable,
            LoggingContext loggingContext)
        {
            Contract.Requires(root.IsValid);

            string rootPath = root.ToString(pathTable);
            if (!FileUtilities.DirectoryExistsNoFollow(rootPath))
            {
                Logger.Log.FailedToRedirectUserProfile(loggingContext, I($"Junction root '{rootPath}' does not exist."));
                return false;
            }

            const string RedirectedUserName = "buildXLUserProfile";

            // get the current user AppData directory path before we make any changes
            string currentUserProfile = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // <root>\RedirectedUserName
            string redirectedProfile = Path.Combine(rootPath, RedirectedUserName);

            var homeDrive = Path.GetPathRoot(redirectedProfile).TrimEnd('\\');
            var homePath = redirectedProfile.Substring(homeDrive.Length);

            // <root>\RedirectedUserName\AppData\
            string appData = Path.Combine(redirectedProfile, "AppData");

            // <root>\RedirectedUserName\AppData\Roaming
            string appDataRoaming = Path.Combine(appData, "Roaming");

            // <root>\RedirectedUserName\AppData\Local
            string appDataLocal = Path.Combine(appData, "Local");

            // <root>\RedirectedUserName\AppData\LocalLow
            string appDataLocalLow = Path.Combine(appData, "LocalLow");

            // <root>\RedirectedUserName\AppData\Local\Microsoft\Windows\INetCache
            string iNetCache = Path.Combine(appDataLocal, "Microsoft", "Windows", "INetCache");

            // <root>\RedirectedUserName\AppData\Local\Microsoft\Windows\History
            string history = Path.Combine(appDataLocal, "Microsoft", "Windows", "History");

            // <root>\RedirectedUserName\AppData\Local\Microsoft\Windows\INetCookies
            string iNetCookies = Path.Combine(appDataLocal, "Microsoft", "Windows", "INetCookies");

            var envVariables = new List<(string name, string original, string redirected)>()
            {
                ("APPDATA" , SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), appDataRoaming),
                ("LOCALAPPDATA" , SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), appDataLocal),
                ("USERPROFILE" , currentUserProfile, redirectedProfile),
                ("USERNAME" , Environment.GetEnvironmentVariable("USERNAME"), RedirectedUserName),
                ("HOMEDRIVE", Environment.GetEnvironmentVariable("HOMEDRIVE"), homeDrive),
                ("HOMEPATH" , Environment.GetEnvironmentVariable("HOMEPATH"), homePath),
                ("INTERNETCACHE" , SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.InternetCache, Environment.SpecialFolderOption.DoNotVerify), iNetCache),
                ("INTERNETHISTORY", SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.History, Environment.SpecialFolderOption.DoNotVerify) , history),
                ("INETCOOKIES", SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.Cookies, Environment.SpecialFolderOption.DoNotVerify), iNetCookies),
                ("LOCALLOW" , FileUtilities.GetKnownFolderPath(FileUtilities.KnownFolderLocalLow), appDataLocalLow),
            };

            var redirectedEnvVariables = envVariables.ToDictionary(entry => entry.name, entry => entry.redirected);

            specialFolderInitializer(redirectedEnvVariables);
            properties.AddRange(redirectedEnvVariables);

            // Setup the junction: <root>\RedirectedUserName => <currentUserProfile>

            var r = FileUtilities.TryProbePathExistence(redirectedProfile, false);
            if (!r.Succeeded)
            {
                Logger.Log.FailedToRedirectUserProfile(loggingContext, I($"Failed to probe the junction for existence ('{redirectedProfile}'). {r.Failure.Describe()}"));
                return false;
            }

            // We need to delete any junction that existed prior to this build.
            // Although a junction is directory with a reparse point, we treat it as a file
            // for the purposes of existence probe; however, we still need to delete it as a directory.
            if (r.Result == PathExistence.ExistsAsFile)
            {
                if (!FileUtilities.TryRemoveDirectory(redirectedProfile, out int errorCode))
                {
                    Logger.Log.FailedToRedirectUserProfile(loggingContext, I($"Failed to delete existing junction '{redirectedProfile}' (error code: '{errorCode}')."));
                    return false;
                }
            }
            else if (r.Result == PathExistence.ExistsAsDirectory)
            {
                Logger.Log.FailedToRedirectUserProfile(loggingContext, I($"'{redirectedProfile}' exists as a directory."));
                return false;
            }

            try
            {
                Directory.CreateDirectory(redirectedProfile);
                FileUtilities.CreateJunction(redirectedProfile, currentUserProfile);
            }
            catch (Exception e)
            {
                Logger.Log.FailedToRedirectUserProfile(loggingContext, I($"Failed to create a junction from '{redirectedProfile}' to '{currentUserProfile}': {e.ToString()}"));
                return false;
            }

            // Some tools use mysterious ways of getting paths under <currentUserProfile>\AppData.
            // Create a directory translation here, this would take care of any potential leaks out of our redirected profile.

            if (!AbsolutePath.TryCreate(pathTable, currentUserProfile, out var fromPath))
            {
                Logger.Log.FailedToRedirectUserProfile(loggingContext, I($"Failed to create an absolute path from '{currentUserProfile}'."));
                return false;
            }

            if (!AbsolutePath.TryCreate(pathTable, redirectedProfile, out var toPath))
            {
                Logger.Log.FailedToRedirectUserProfile(loggingContext, I($"Failed to create an absolute path from '{redirectedProfile}'."));
                return false;
            }

            redirectedDirectories.Add(new TranslateDirectoryData(I($"{currentUserProfile}<{redirectedProfile}"), fromPath, toPath));

            Logger.Log.UsingRedirectedUserProfile(
                loggingContext, 
                currentUserProfile, 
                redirectedProfile,
                string.Join(Environment.NewLine, envVariables.Select(entry => I($"{entry.name}: '{entry.original}' -> '{entry.redirected}'"))));
            return true;
        }

        private static bool ValidateSubstAndDirectoryTranslation(ConfigurationImpl mutableConfig, PathTable pathTable, LoggingContext loggingContext)
        {
            var translations = JoinSubstAndDirectoryTranslation(mutableConfig, pathTable);
            string error;

            if (!DirectoryTranslator.ValidateDirectoryTranslation(pathTable, translations, out error))
            {
                Logger.Log.InvalidDirectoryTranslation(loggingContext, error);
                return false;
            }

            if (mutableConfig.InCloudBuild() && !DirectoryTranslator.TestForJunctions(pathTable, translations, out error))
            {
                Logger.Log.DirectoryTranslationsDoNotPassJunctionTest(loggingContext, Environment.NewLine + error);
            }

            return true;
        }

        private static List<DirectoryTranslator.RawInputTranslation> JoinSubstAndDirectoryTranslation(IConfiguration config, PathTable pathTable)
        {
            var translations = new List<DirectoryTranslator.RawInputTranslation>();

            if (config.Logging.SubstTarget.IsValid && config.Logging.SubstSource.IsValid)
            {
                translations.Add(DirectoryTranslator.RawInputTranslation.Create(
                    config.Logging.SubstSource,
                    config.Logging.SubstTarget,
                    config.Logging.SubstSource.ToString(pathTable) + "<" + config.Logging.SubstTarget.ToString(pathTable)));
            }

            translations.AddRange(config.Engine.DirectoriesToTranslate.Select(td => DirectoryTranslator.RawInputTranslation.Create(td.FromPath, td.ToPath, td.RawUserOption)));

            return translations;
        }

        private static bool ValidateModuleConfig(
            IOneBuildModuleConfiguration moduleConfig,
            PathTable pathTable,
            LoggingContext loggingContext,
            SandboxConfiguration sandboxConfig)
        {
            bool success = true;

            // Engine settings for whitelists.
            // NOTE: This is temporarily duplicated in the ConfigFileState.MergeIntoConfiguration for when the config is loaded from Cached Graph. This is scheduled for a later cleanup
            if (moduleConfig.FileAccessWhiteList.Count > 0 || moduleConfig.CacheableFileAccessWhitelist.Count > 0)
            {
                sandboxConfig.FailUnexpectedFileAccesses = false;
                Logger.Log.FileAccessManifestSummary(
                    loggingContext,
                    moduleConfig.Name,
                    moduleConfig.CacheableFileAccessWhitelist.Count,
                    moduleConfig.FileAccessWhiteList.Count);
            }

            var allRules = new Dictionary<AbsolutePath, IDirectoryMembershipFingerprinterRule>();
            foreach (var rule in moduleConfig.DirectoryMembershipFingerprinterRules)
            {
                var location = rule.Location.IsValid ? rule.Location : moduleConfig.Location;

                // Need to check for duplicate rules when merging configs since rules can be defined across multiple config files
                IDirectoryMembershipFingerprinterRule existingRule;
                if (allRules.TryGetValue(rule.Root, out existingRule))
                {
                    Logger.Log.DuplicateDirectoryMembershipFingerprinterRule(
                        loggingContext,
                        location.Path.ToString(pathTable),
                        existingRule.Name,
                        rule.Name,
                        rule.Root.ToString(pathTable));

                    success = false;
                }
                else
                {
                    allRules.Add(rule.Root, rule);
                }

                if (!(rule.DisableFilesystemEnumeration ^ rule.FileIgnoreWildcards.Count >= 1))
                {
                    Logger.Log.DirectoryMembershipFingerprinterRuleError(
                        loggingContext,
                        rule.Name,
                        rule.Root.ToString(pathTable));
                    success = false;
                }
            }

            var symbolTableForValidation = new SymbolTable(pathTable.StringTable);
            foreach (var whiteListEntry in moduleConfig.FileAccessWhiteList.Union(moduleConfig.CacheableFileAccessWhitelist))
            {
                var location = whiteListEntry.Location.IsValid ? whiteListEntry.Location : moduleConfig.Location;
                SerializableRegex pathRegex;
                string error;

                if (string.IsNullOrEmpty(whiteListEntry.PathRegex))
                {
                    if (!FileAccessWhitelist.TryCreateWhitelistRegex(Regex.Escape(whiteListEntry.PathFragment), out pathRegex, out error))
                    {
                        throw Contract.AssertFailure("A whitelist regex should never fail to construct from an escaped pattern.");
                    }
                }
                else
                {
                    if (!FileAccessWhitelist.TryCreateWhitelistRegex(whiteListEntry.PathRegex, out pathRegex, out error))
                    {
                        Logger.Log.FileAccessWhitelistEntryHasInvalidRegex(
                            loggingContext,
                            location.Path.ToString(pathTable),
                            location.Line,
                            location.Position,
                            error);
                        success = false;
                    }
                }

                if (!string.IsNullOrEmpty(whiteListEntry.Value))
                {
                    FullSymbol symbol;
                    int characterWithError;
                    if (FullSymbol.TryCreate(symbolTableForValidation, whiteListEntry.Value, out symbol, out characterWithError) !=
                        FullSymbol.ParseResult.Success)
                    {
                        Logger.Log.FileAccessWhitelistCouldNotCreateIdentifier(
                            loggingContext,
                            location.Path.ToString(pathTable),
                            location.Line,
                            location.Position,
                            whiteListEntry.Value);
                        success = false;
                    }
                }
            }

            return success;
        }

        /// <summary>
        /// Returns true if at least one pip failure is caused by a lost connectivity with a worker.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public bool HasInfrastructureFailures
        {
            get
            {
                return m_masterService != null && m_masterService.HasInfrastructureFailures;
            }
        }

        /// <summary>
        /// Perf counter collector for the session. This may be null if perf counter collection is not enabled
        /// </summary>
        private readonly PerformanceCollector m_collector;

        /// <summary>
        /// Sets the visualization information to be collected
        /// </summary>
        public void SetVisualizationInformation(EngineLiveVisualizationInformation visualization)
        {
            Contract.Requires(visualization != null);

            m_visualization = visualization;
        }

        /// <summary>
        /// Sets the snapshot visitor
        /// </summary>
        public void SetSnapshotCollector(SnapshotCollector snapshotCollector)
        {
            Contract.Requires(snapshotCollector != null);

            m_snapshotCollector = snapshotCollector;
        }

        /// <summary>
        /// Runs the engine, producing the output values requested by the arguments.
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode")]
        public BuildXLEngineResult Run(LoggingContext loggingContext, EngineState engineState = null)
        {
            return DoRun(loggingContext, engineState, disposeFrontEnd: true);
        }

        /// <summary>
        /// Runs the engine for testing purposes, producing the output values requested by the arguments.
        /// </summary>
        /// <remarks>
        /// The FrontEndHost is not disposed in this case, and callers are responsible for that. This allows
        /// tests to inspect the host after the engine has run.
        /// </remarks>
        public BuildXLEngineResult RunForFrontEndTests(LoggingContext loggingContext, EngineState engineState = null)
        {
            return DoRun(loggingContext, engineState, disposeFrontEnd: false);
        }

        private VolumeMap TryGetVolumeMapOfAllLocalVolumes(PerformanceMeasurement pm, LoggingContext loggingContext)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                return null;
            }

            IReadOnlyList<string> junctionRoots = Configuration.Engine.DirectoriesToTranslate?.Select(a => a.ToPath.ToString(Context.PathTable)).ToList();
            VolumeMap volumeMap = VolumeMap.TryCreateMapOfAllLocalVolumes(loggingContext, junctionRoots);

            if (volumeMap == null)
            {
                Contract.Assume(pm.LoggingContext.ErrorWasLogged, "An error should have been logged during local volumes map creation.");
            }

            return volumeMap;
        }

        private JournalState GetJournalStateWithVolumeMap(VolumeMap volumeMap, LoggingContext loggingContext)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                return JournalState.DisabledJournal;
            }

            Contract.Requires(volumeMap != null);
            Contract.Requires(loggingContext != null);

            // Under some configurations, we access the change journal (i.e., actually scan the journal).
            if (Configuration.Engine.ScanChangeJournal)
            {
                var maybeJournal = JournalAccessorGetter.TryGetJournalAccessor(
                    volumeMap,
                    m_initialCommandLineConfiguration.Startup.ConfigFile.ToString(Context.PathTable));

                if (maybeJournal.Succeeded)
                {
                    return JournalState.CreateEnabledJournal(volumeMap, maybeJournal.Result);
                }

                Logger.Log.FailedToGetJournalAccessor(loggingContext, maybeJournal.Failure.Describe());
            }

            return JournalState.DisabledJournal;
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode")]
        private BuildXLEngineResult DoRun(LoggingContext loggingContext, EngineState engineState = null, bool disposeFrontEnd = true)
        {
            Contract.Requires(engineState == null || Configuration.Engine.ReuseEngineState);
            Contract.Ensures(
                !(Contract.Result<BuildXLEngineResult>().IsSuccess == false && loggingContext.ErrorWasLogged == false),
                "Engine.Run() returns a failure but no error was logged.");
            Contract.Ensures(
                !(Contract.Result<BuildXLEngineResult>().IsSuccess == true && loggingContext.ErrorWasLogged == true),
                "Engine.Run() returns a success even though an error was logged.");
            Contract.Ensures(EngineState.CorrectEngineStateTransition(engineState, Contract.Result<BuildXLEngineResult>().EngineState, out var message), message);

            if (m_distributionService != null && !m_distributionService.Initialize())
            {
                return BuildXLEngineResult.Failed(engineState);
            }

            if (Configuration.Experiment.ForceContractFailure)
            {
                Contract.Assert(false, "Assert induced by /exp:ForceContractFailure on command line.");
            }

            if (!OperatingSystemHelper.IsUnixOS)
            {
                if (TestHooks == null || TestHooks.DoWarnForVirusScan)
                {
                    WarnForVirusScan(loggingContext, Configuration.Layout);
                }
            }
            else
            {
                if (Configuration.Layout.EmitSpotlightIndexingWarning)
                {
                    void CheckArtifactFolersAndEmitNoIndexWarning(params AbsolutePath[] paths)
                    {
                        var directories = paths.Select(p => p.ToString(Context.PathTable)).Where(p => !p.EndsWith(Strings.Layout_DefaultNoIndexSuffix));
                        if (directories.Count() > 0)
                        {
                            Logger.Log.EmitSpotlightIndexingWarningForArtifactDirectory(loggingContext, string.Join(", ", directories));
                        }
                    }

                    CheckArtifactFolersAndEmitNoIndexWarning(
                        Configuration.Layout.ObjectDirectory,
                        Configuration.Layout.CacheDirectory,
                        Configuration.Layout.FrontEndDirectory,
                        Configuration.Layout.EngineCacheDirectory);
                }

                // Make sure we are running on a case-insensitive file system in the macOS/Unix case for the time being
                if (FileUtilities.IsFileSystemCaseSensitive())
                {
                    Logger.Log.ErrorCaseSensitiveFileSystemDetected(loggingContext);
                    return BuildXLEngineResult.Failed(engineState);
                }
            }

            // Changing the min count of threads in the thread pool to speed-up CPU and IO intensive operations during front-end phase.
            ThreadPoolHelper.ConfigureWorkerThreadPools(Configuration.Schedule.MaxProcesses, Configuration.FrontEnd.ThreadPoolMinThreadCountMultiplier());
            ConfigureServicePointManager();

            EngineState newEngineState = null;
            bool success = true;

            using (var pm = PerformanceMeasurement.StartWithoutStatistic(
                loggingContext,
                Logger.Log.StartEngineRun,
                Logger.Log.EndEngineRun))
            {
                bool directoryDeletionLockSuccess;
                using (CreateOutputDirectories(Context.PathTable, pm.LoggingContext, out directoryDeletionLockSuccess))
                {
                    if (!directoryDeletionLockSuccess)
                    {
                        Contract.Assume(pm.LoggingContext.ErrorWasLogged, "An error should have been logged during output directory creation.");
                        return BuildXLEngineResult.Failed(engineState);
                    }

                    // Once output directories including MoveDeleteTempDirectory have been created,
                    // create a TempCleaner for cleaning all temp directories
                    m_tempCleaner = new TempCleaner(tempDirectory: m_moveDeleteTempDirectory);

                    using (
                        var objFolderLock = FolderLock.Take(
                            pm.LoggingContext,
                            Configuration.Layout.ObjectDirectory.ToString(Context.PathTable),
                            Configuration.Engine.BuildLockPollingIntervalSec,
                            Configuration.Engine.BuildLockWaitTimeoutMins))
                    {
                        if (!objFolderLock.SuccessfullyCreatedLock)
                        {
                            Contract.Assume(pm.LoggingContext.ErrorWasLogged, "An error should have been logged during folder lock acquisition.");
                            return BuildXLEngineResult.Failed(engineState);
                        }

                        using (
                            var engineCacheLock = FolderLock.Take(
                                pm.LoggingContext,
                                Configuration.Layout.EngineCacheDirectory.ToString(Context.PathTable),
                                Configuration.Engine.BuildLockPollingIntervalSec,
                                Configuration.Engine.BuildLockWaitTimeoutMins))
                        {
                            if (!engineCacheLock.SuccessfullyCreatedLock)
                            {
                                Contract.Assume(pm.LoggingContext.ErrorWasLogged, "An error should have been logged during folder lock acquisition.");
                                return BuildXLEngineResult.Failed(engineState);
                            }

                            if (!LogAndValidateConfiguration(pm.LoggingContext))
                            {
                                Contract.Assume(pm.LoggingContext.ErrorWasLogged, "An error should have been logged during configuration validation.");
                                return BuildXLEngineResult.Failed(engineState);
                            }

                            var recovery = FailureRecoveryFactory.Create(pm.LoggingContext, Context.PathTable, Configuration);
                            bool recoveryStatus = recovery.TryRecoverIfNeeded();

                            var volumeMap = TryGetVolumeMapOfAllLocalVolumes(pm, loggingContext);
                            var journalState = GetJournalStateWithVolumeMap(volumeMap, loggingContext);

                            if (!OperatingSystemHelper.IsUnixOS)
                            {
                                if (volumeMap == null)
                                {
                                    return BuildXLEngineResult.Failed(engineState);
                                }
                            }

                            // Returns stub if explicitly not use file content table.
                            m_fileContentTask = Configuration.Engine.UseFileContentTable == false 
                                ? Task.FromResult(FileContentTable.CreateStub())
                                : FileContentTable.LoadOrCreateAsync(
                                    Configuration.Layout.FileContentTableFile.ToString(Context.PathTable),
                                    Configuration.Cache.FileContentTableEntryTimeToLive ?? FileContentTable.DefaultTimeToLive);

                            EngineSchedule engineSchedule = null;

                            // Task representing the async initialization of this engine's cache.
                            // Cache initialization can be long-running, so we pass around this init task so that consumers can choose
                            // to wait on close to when it is actually needed.
                            CacheInitializationTask cacheInitializationTask = null;
                            ConstructScheduleResult constructScheduleResult = ConstructScheduleResult.None;

                            if (Configuration.Engine.TrackBuildsInUserFolder &&
                                !Configuration.InCloudBuild() &&
                                Configuration.Distribution.BuildRole == DistributedBuildRoles.None)
                            {
                                using (Context.EngineCounters.StartStopwatch(EngineCounter.RecordingBuildsInUserFolderDuration))
                                {
                                    var primaryConfigFile = Configuration.Layout.PrimaryConfigFile.ToString(Context.PathTable);
                                    var logsDirectory = Configuration.Logging.LogsDirectory.ToString(Context.PathTable);
                                    var binDirectory = Configuration.Layout.BuildEngineDirectory.ToString(Context.PathTable);
                                    if (m_translator != null)
                                    {
                                        primaryConfigFile = m_translator.Translate(primaryConfigFile);
                                        logsDirectory = m_translator.Translate(logsDirectory);
                                        binDirectory = m_translator.Translate(binDirectory);
                                    }

                                    new Invocations().RecordInvocation(
                                        loggingContext,
                                        new Invocations.Invocation(
                                            loggingContext.Session.Id,
                                            m_processStartTimeUtc,
                                            primaryConfigFile,
                                            logsDirectory,
                                            m_buildVersion,
                                            binDirectory,
                                            m_commitId
                                        ));

                                    if (Configuration.Viewer == ViewerMode.Show)
                                    {
                                        LaunchBuildExplorer(loggingContext, binDirectory);
                                    }
                                }

                            }

                            try
                            {
                                if (Configuration.Distribution.BuildRole == DistributedBuildRoles.Worker)
                                {
                                    if (!m_workerService.WaitForMasterAttach())
                                    {
                                        // Worker timeout logs a warning but no error. It is not considered a failure wrt the worker
                                        engineState?.Dispose();
                                        Contract.Assert(
                                            pm.LoggingContext.ErrorWasLogged,
                                            "An error should have been logged during waiting for attaching to the master.");
                                        return BuildXLEngineResult.Create(success: false, perfInfo: null, previousState: engineState, newState: null);
                                    }
                                }

                                cacheInitializationTask = CacheInitializer.GetCacheInitializationTask(
                                    loggingContext,
                                    Context.PathTable,
                                    Configuration.Layout.CacheDirectory.ToString(Context.PathTable),
                                    Configuration.Cache,
                                    rootTranslator: m_rootTranslator,
                                    recoveryStatus: recoveryStatus,
                                    cancellationToken: Context.CancellationToken,
                                    testHookCacheFactory: TestHooks?.CacheFactory);

                                // When distribution is on, we have to finish initializing the EngineCache right away.
                                // (rather than trying to defer waiting as much as possible).

                                // Ensure the EngineCache initialized correctly if this is a distributed build
                                if (Configuration.Distribution.BuildRole != DistributedBuildRoles.None)
                                {
                                    Possible<CacheInitializer> possibleCacheInitializerForDistribution =
                                        cacheInitializationTask.GetAwaiter().GetResult();

                                    if (!possibleCacheInitializerForDistribution.Succeeded)
                                    {
                                        // StorageCacheStartupError has been logged by CacheInitializer
                                        Logger.Log.ErrorCacheDisabledDistributedBuild(pm.LoggingContext);
                                        return BuildXLEngineResult.Failed(engineState);
                                    }
                                }

                                RootFilter rootFilter;
                                constructScheduleResult = ConstructSchedule(
                                    loggingContext,
                                    Configuration.FrontEnd.MaxFrontEndConcurrency(),
                                    cacheInitializationTask,
                                    journalState,
                                    engineState,
                                    out engineSchedule,
                                    out rootFilter);
                                success &= constructScheduleResult != ConstructScheduleResult.Failure;
                                ValidateSuccessMatches(success, pm.LoggingContext);

                                var phase = Configuration.Engine.Phase;

                                if (success && phase.HasFlag(EnginePhases.Schedule) && !Configuration.Schedule.DisableProcessRetryOnResourceExhaustion)
                                {
                                    // TODO: update this once shared opaques play nicely with resource based cancellation
                                    // Resource based cancellation might lead to wrong cache entries.
                                    // Consider this scenario:
                                    // - a pip is to produce a shared opaque directory dir and files a, b, c, and d inside of this directory
                                    // - the pip is cancelled after it has produced files dir/a and dir/b
                                    // - pip is re-run
                                    // - before producing dir/a and dir/b, the pip probes those files for existence and decides not to produce them;
                                    //   the pip produces dir/c and dir/d, and finishes successfully
                                    // - while processing outputs in shared opaques, we rely on a list of write accesses reported by detours;
                                    //   since the pip only probed files a and b, we do not treat them as pip's output => cache entry does not contain
                                    //   full output of the pip

                                    var sharedOpaqueDir = engineSchedule.Scheduler.PipGraph.AllSealDirectories.FirstOrDefault(directoryArtifact => directoryArtifact.IsSharedOpaque);
                                    if (sharedOpaqueDir.IsValid)
                                    {
                                        success = false;
                                        Logger.Log.ResourceBasedCancellationIsEnabledWithSharedOpaquesPresent(pm.LoggingContext, sharedOpaqueDir.Path.ToString(Context.PathTable));
                                    }
                                    ValidateSuccessMatches(success, pm.LoggingContext);
                                }

                                if (success && phase.HasFlag(EnginePhases.Schedule) && Configuration.Ide.IsEnabled)
                                {
                                    Contract.Assert(engineSchedule != null);

                                    IdeGenerator.Generate(
                                        engineSchedule.Context,
                                        engineSchedule.Scheduler.PipGraph,
                                        engineSchedule.Scheduler.ScheduledGraph,
                                        m_initialCommandLineConfiguration.Startup.ConfigFile,
                                        Configuration.Ide);
                                }

                                // Front end is no longer needed and can be clean-up before moving to a next phase.
                                // It make no sense to check for error case.
                                // If there is an error, the process will be closed any way.
                                if (disposeFrontEnd)
                                {
                                    CleanUpFrontEndOnSuccess(success, constructScheduleResult);
                                }

                                // Build workers don't allow CleanOnly builds since the master selects which pips they run
                                if (success && Configuration.Engine.CleanOnly && Configuration.Distribution.BuildRole != DistributedBuildRoles.Worker)
                                {
                                    Contract.Assert(
                                        !phase.HasFlag(EnginePhases.Execute),
                                        "CleanOnly in conjunction with executing pips doesn't make sense since output file cleaning happens again before each pip run.");
                                    Contract.Assert(rootFilter != null);

                                    Func<DirectoryArtifact, bool> isOutputDir = artifact =>
                                    {
                                        NodeId node = engineSchedule.Scheduler.PipGraph.GetSealedDirectoryNode(artifact);
                                        SealDirectoryKind sealDirectoryKind = engineSchedule.Scheduler.PipGraph.PipTable.GetSealDirectoryKind(node.ToPipId());

                                        return sealDirectoryKind == SealDirectoryKind.Opaque || sealDirectoryKind == SealDirectoryKind.SharedOpaque;
                                    };

                                    success &= OutputCleaner.DeleteOutputs(
                                        pm.LoggingContext,
                                        isOutputDir,
                                        engineSchedule.Scheduler.PipGraph.FilterOutputsForClean(
                                            rootFilter,
                                            Configuration.Schedule.CanonicalizeFilterOutputs).ToArray(),
                                        Context.PathTable,
                                        m_tempCleaner);
                                    ValidateSuccessMatches(success, pm.LoggingContext);
                                }

                                // Keep this as close to the Execute phase as possible
                                if (phase.HasFlag(EnginePhases.Schedule) && Configuration.Engine.LogStatistics)
                                {
                                    BuildXL.Tracing.Logger.Log.Statistic(
                                        pm.LoggingContext,
                                        new Statistic
                                        {
                                            Name = Statistics.TimeToFirstPipSyntheticMs,
                                            Value = (int)(DateTime.UtcNow - m_processStartTimeUtc).TotalMilliseconds,
                                        });
                                }

                                ThreadPoolHelper.ConfigureWorkerThreadPools(Configuration.Schedule.MaxProcesses);

                                if (success && phase.HasFlag(EnginePhases.Execute))
                                {
                                    Contract.Assert(
                                        phase.HasFlag(EnginePhases.Schedule),
                                        "Must have already scheduled the values. It should be impossible to Execute without scheduling.");
                                    Contract.Assert(engineSchedule != null, "Scheduler is non-null when we are at least in the Schedule phase.");

                                    WorkerService workerservice = null;

                                    if (Configuration.Distribution.BuildRole != DistributedBuildRoles.None)
                                    {
                                        if (Configuration.Distribution.BuildRole == DistributedBuildRoles.Master)
                                        {
                                            m_masterService.EnableDistribution(engineSchedule);
                                        }
                                        else if (Configuration.Distribution.BuildRole == DistributedBuildRoles.Worker)
                                        {
                                            workerservice = m_workerService;
                                        }
                                    }

                                    var stats = default(ExecuteStatistics);
                                    using (var executePhase = StartPhase(
                                        loggingContext,
                                        EnginePhases.Execute,
                                        Logger.Log.ExecutePhaseStart,
                                        (context, executeStats) =>
                                        {
                                            m_enginePerformanceInfo.LimitingResourcePercentages = engineSchedule.Scheduler.ExecutionSampler.GetLimitingResourcePercentages();
                                            Logger.Log.ExecutePhaseComplete(
                                                context,
                                                executeStats,
                                                m_enginePerformanceInfo.LimitingResourcePercentages);
                                            m_enginePerformanceInfo.ExecutePhaseDurationMs = executeStats.ElapsedMilliseconds;
                                        },
                                        () => stats))
                                    {
                                        var executePhaseLoggingContext = executePhase.LoggingContext;
                                        Contract.Assert(
                                            executePhaseLoggingContext.GetRootContext() == loggingContext.GetRootContext(),
                                            "PhaseLoggingContext's root context doesn't match AppLoggingContext's root.");
                                        Contract.Assert(
                                            executePhaseLoggingContext.GetRootContext() == pm.LoggingContext.GetRootContext(),
                                            "PhaseLoggingContext's root context doesn't match pm's root.");
                                        success &= engineSchedule.ExecuteScheduledPips(
                                            executePhaseLoggingContext,
                                            workerservice,
                                            Configuration.Logging);
                                        ValidateSuccessMatches(success, pm.LoggingContext);
                                    }
                                }

                                Task<bool> postExecutionTasks = Task.FromResult(true);

                                // Post execution tasks are only performed on master.
                                if (Configuration.Distribution.BuildRole != DistributedBuildRoles.Worker && engineSchedule != null)
                                {
                                    // Even if the execution phase failed or was skipped, we want to persist / finish persisting some structures like the exported pip graph.
                                    // TODO: Should this also happen in the event we have a fully constructed engine schedule, but e.g. evaluation failures?
                                    //       Need to quantify how successful schedule construction was, and branch in ProcessEndOfBuild accordingly.

                                    postExecutionTasks = engineSchedule.ProcessPostExecutionTasksAsync(
                                        loggingContext,
                                        Context,
                                        Configuration,
                                        phase);
                                }

                                // The file content table should now contain all of the hashes needed for this build.
                                // At this point we may have failed (but have not exploded), so it is safe to write it out. Note that we might want to write it
                                // out even if we did not run the execution phase at all, due to file content table usage before then (e.g. cached graph reloading).
                                var savingFileContentTableTask = FileContentTable.IsStub
                                    ? Task.FromResult(true)
                                    : TrySaveFileContentTable(loggingContext);

                                // Saving file content table and post execution tasks are happening in parallel.
                                success &= postExecutionTasks.GetAwaiter().GetResult();
                                ValidateSuccessMatches(success, pm.LoggingContext);

                                if (!savingFileContentTableTask.GetAwaiter().GetResult())
                                {
                                    Contract.Assert(
                                        pm.LoggingContext.ErrorWasLogged,
                                        "An error should have been logged during saving file content table.");
                                    return BuildXLEngineResult.Failed(engineState);
                                }
                            }
                            finally
                            {
                                // Ensure the task that saves the graph to the content cache has finished
                                try
                                {
                                    m_graphCacheContentCachePut?.GetAwaiter().GetResult();
                                }
                                catch (Exception e)
                                {
                                    BuildXL.Tracing.Logger.Log.UnexpectedCondition(loggingContext, e.ToString());
                                }

                                // Ensure the execution log supporting graph files have finished copying to the logs directory
                                m_executionLogGraphCopy?.GetAwaiter().GetResult();
                                m_previousInputFilesCopy?.GetAwaiter().GetResult();

                                LogStats(loggingContext, engineSchedule, cacheInitializationTask, constructScheduleResult);

                                Context.EngineCounters.MeasuredDispose(m_masterService, EngineCounter.MasterServiceDisposeDuration);

                                Context.EngineCounters.MeasuredDispose(m_workerService, EngineCounter.WorkerServiceDisposeDuration);

                                if (Configuration.Engine.ReuseEngineState)
                                {
                                    // There are three places where EngineState get disposed:
                                    // (1) When a new engine schedule is constructed from scratch, we dispose the existing engine state.
                                    // (2) When the graph cache is a hit and BuildXL tries to reload the graph from the engine state, the engine state gets disposed if the engine state and engine cache dir are out-of-sync.
                                    //     this happens when the engine cache dir is manually edited externally or another BuildXL build (non-server) changed the engine cache dir during the lifetime of the engine state.
                                    //     EngineState.Version property and EngineState file in the engineCache dir allow us to understand whether they are in-sync.
                                    // (3) On creating failed BuildXLEngineResult.
                                    if (engineSchedule != null)
                                    {
                                        // If the build graph is successfully created, then we create one or reuse based on EngineSchedule
                                        newEngineState = engineSchedule.GetOrCreateNewEngineState(engineState);
                                    }
                                    else
                                    {
                                        // If the build is unsuccessful, i.e., engineSchedule is null, and engine state is not disposed, then reuse the existing engine state.
                                        newEngineState = engineState != null && engineState.IsDisposed ? null : engineState;
                                    }

                                    Contract.Assume(EngineState.CorrectEngineStateTransition(engineState, newEngineState, out var incorrectMessage), incorrectMessage);
                                }

                                if (engineSchedule != null)
                                {
                                    // We cannot dispose the PipTable in the EngineSchedule if TestHooks or Visualization is enabled.
                                    // We transfer PipTable ownership to TestHooks or EngineLiveVisualizationInformation if at least one of them are enabled.
                                    if (TestHooks?.Scheduler != null)
                                    {
                                        var isTransferred = engineSchedule.TransferPipTableOwnership(TestHooks.Scheduler.Value.PipGraph.PipTable);
                                        Contract.Assume(isTransferred);
                                    }

                                    if (m_visualization?.PipTable.State == VisualizationValueState.Available)
                                    {
                                        var isTransferred = engineSchedule.TransferPipTableOwnership(m_visualization.PipTable.Value);
                                        Contract.Assume(isTransferred);
                                    }

                                    // Dispose engineSchedule before disposing EngineCache
                                    // Do not move this statement after engineCacheInitialization.GetAwaiter().GetResult() below
                                    engineSchedule.Dispose();
                                }

                                using (Context.EngineCounters.StartStopwatch(EngineCounter.EngineCacheInitDisposeDuration))
                                {
                                    // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                                    cacheInitializationTask?.GetAwaiter().GetResult().Then(
                                        cacheInitializer =>
                                        {
                                            // Check for successful cache session close, otherwise fail the build
                                            Possible<string, Failure> cacheCloseResult = cacheInitializer.Close();
                                            if (!cacheCloseResult.Succeeded)
                                            {
                                                Logger.Log.CacheSessionCloseFailed(
                                                    loggingContext,
                                                    cacheCloseResult.Failure.DescribeIncludingInnerFailures());
                                                success = false;
                                            }
                                            Context.EngineCounters.MeasuredDispose(cacheInitializer, EngineCounter.EngineCacheDisposeDuration);
                                            return Unit.Void;
                                        });
                                }

                                cacheInitializationTask?.Dispose();

                                if (m_snapshotCollector != null)
                                {
                                    using (Context.EngineCounters.StartStopwatch(EngineCounter.SnapshotCollectorPersistDuration))
                                    {
                                        success &= m_snapshotCollector.Persist(Configuration, Context.PathTable, Context.CancellationToken);
                                        ValidateSuccessMatches(success, pm.LoggingContext);
                                    }
                                }

                                if (Configuration.Engine.LogStatistics)
                                {
                                    Context.EngineCounters.LogAsStatistics("Engine", loggingContext);
                                }
                            }
                        } // End of EngineCache mutex lock
                    } // End of object directory mutex Lock
                } // End of output directory deletion lock
            } // End of perf measurement

            if (ShouldUpgradeFileAccessWarningsToHighLevelError(Configuration) &&
                m_trackingEventListener != null &&
                ((m_trackingEventListener.CountsPerEventId(EventId.FileMonitoringWarning) != 0) ||
                 (m_trackingEventListener.CountsPerEventId(EventId.PipProcessDisallowedNtCreateFileAccessWarning) != 0)))
            {
                Logger.Log.FileAccessErrorsExist(loggingContext);
                success = false;
            }

            ValidateSuccessMatches(success, loggingContext);

            return BuildXLEngineResult.Create(success, m_enginePerformanceInfo, previousState: engineState, newState: newEngineState);
        }


        private static void LaunchBuildExplorer(LoggingContext loggingContext, string engineBinDirectory)
        {
            var bxpExe = Path.Combine(engineBinDirectory, "tools", "bxp", "bxp.exe");
            if (!File.Exists(bxpExe))
            {
                Logger.Log.FailureLaunchingBuildExplorerFileNotFound(loggingContext, bxpExe);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(
                    new ProcessStartInfo()
                    {
                        FileName = bxpExe,
                        Arguments = loggingContext.Session.Id,
                        ErrorDialog = false, // Dont show error if something goes wrong
                        UseShellExecute = true, // Launch through shell to trampoline process tree
                    });
            }
            catch (Win32Exception e)
            {
                Logger.Log.FailureLaunchingBuildExplorerException(loggingContext, e.Message);
            }
            catch (ObjectDisposedException e)
            {
                Logger.Log.FailureLaunchingBuildExplorerException(loggingContext, e.Message);
            }
            catch (FileNotFoundException e)
            {
                Logger.Log.FailureLaunchingBuildExplorerException(loggingContext, e.Message);
            }
        }

        private static bool ShouldUpgradeFileAccessWarningsToHighLevelError(IConfiguration configuration)
        {
            return !configuration.Logging.FailPipOnFileAccessError && !configuration.Sandbox.FailUnexpectedFileAccesses;
        }

        private DirectoryTranslator CreateDirectoryTranslator(IConfiguration configuration)
        {
            var translations = JoinSubstAndDirectoryTranslation(configuration, Context.PathTable);
            var translator = new DirectoryTranslator();

            translator.AddTranslations(translations, Context.PathTable);
            translator.Seal();

            return translator;
        }

        private void ValidateSuccessMatches(bool success, LoggingContext loggingContext)
        {
            // When cancellation is requested, an error is logged. But not all tasks are long running enough to
            // check the cancellation token. So a step may be successful even when an error is logged. Don't validate
            // success and the error logging matches on cancellation since the build will terminate anyway.
            if (Context.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (success)
            {
                if (loggingContext.ErrorWasLogged)
                {
                    throw new BuildXLException("No error should be logged if status is success. " +
                        "Errors logged: " + string.Join(", ", loggingContext.ErrorsLoggedById.ToArray()));
                }
            }
            else
            {
                Contract.Assert(loggingContext.ErrorWasLogged, "Error should have been logged for non-success");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods")]
        private void CleanUpFrontEndOnSuccess(bool success, ConstructScheduleResult constructScheduleResult)
        {
            // The front end controller is always disposed, independently of succeeding.
            FrontEndController.Dispose();

            if (success && constructScheduleResult == ConstructScheduleResult.ConstructedNewGraph)
            {
                // This is very important to release the reference to a front end controller
                // and collect the garbage.
                // Front end itself is very heavy and this code releases gigs of memory for a large build.
                FrontEndController = null;

                // Frontend allocates a lot of large objects for parsing.
                // Triggering large object heap compaction
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

                // In general this is dangerous API, but we know what we're doing. Right?
                GC.Collect();

                // TODO: Investigate if this check is always reliable, and in that case, remove the configuration flag check
                if (Configuration.FrontEnd.FailIfFrontendMemoryIsNotCollected() && FrontEndControllerMemoryObserver.IsFrontEndAlive())
                {
                    // This is a real problem that needs to be addressed.
                    // Crashing the app in this case.
                    throw new InvalidOperationException("Front end memory is not released successfully.");
                }
            }
        }

        private static void ConfigureServicePointManager()
        {
            // As of .NET 4.6 ServicePointManager defaults to int.MaxValue; in .NET 4.5.2 or less,
            // the default value is 2 which is way too low for us.  The primary consumer of Http at this point is Cache.
            // Each cache query makes approximately 1 GetSelectorsCall, 1 concurrent GetContentHashList call
            // and up to 2 additional blob calls to (pre-)fetch files.
            ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        }

        /// <summary>
        /// Creates output directories for the engine, and returns a disposable object that (until disposed)
        /// prevents them from being deleted.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public DirectoryDeletionLock CreateOutputDirectories(PathTable pathTable, LoggingContext loggingContext, out bool success)
        {
            Contract.Requires(pathTable != null);

            var locker = new DirectoryDeletionLock();
            var layout = Configuration.Layout;
            success = false;
            try
            {
                locker.CreateAndPreventDeletion(layout.ObjectDirectory.ToString(pathTable));
                locker.CreateAndPreventDeletion(layout.CacheDirectory.ToString(pathTable));
                locker.CreateAndPreventDeletion(layout.EngineCacheDirectory.ToString(pathTable));
                locker.CreateAndPreventDeletion(Configuration.Logging.LogsDirectory.ToString(pathTable));

                if (Configuration.Logging.RedirectedLogsDirectory.IsValid)
                {
                    locker.CreateRedirectionAndPreventDeletion(
                        Configuration.Logging.RedirectedLogsDirectory.ToString(pathTable), 
                        Configuration.Logging.LogsDirectory.ToString(pathTable), 
                        deleteExisting: true,
                        deleteOnClose: true);
                }
                
                locker.CreateAndPreventDeletion(m_moveDeleteTempDirectory);

                success = true;
            }
            catch (BuildXLException ex)
            {
                Logger.Log.FailedToAcquireDirectoryDeletionLock(loggingContext, ex.Message);
            }
            finally
            {
                if (!success)
                {
                    locker.Dispose();
                }
            }

            return locker;
        }

        /// <summary>
        /// Creates a dictionary of all unsafe options mapped to their associated logger functions.
        /// This should be added to every time a new unsafe option is added.
        /// </summary>
        public static Dictionary<string, Action<LoggingContext>> CreateUnsafeOptionLoggers()
        {
            // Associate unsafe command line options with logger warnings
            // If an unsafe option was added to args.cs, a new logging function should be added
            return new Dictionary<string, Action<LoggingContext>>(StringComparer.OrdinalIgnoreCase)
            {
                { "unsafe_AllowCopySymlink", loggingContext => { } /* Special case: unsafe option we do not want logged */ },
                { "unsafe_AllowMissingOutput", Logger.Log.ConfigUnsafeAllowMissingOutput },
                { "unsafe_DisableCycleDetection", Logger.Log.ConfigUnsafeDisableCycleDetection },
                { "unsafe_DisableDetours", Logger.Log.ConfigDisableDetours },
                { "unsafe_DisableGraphPostValidation", loggingContext => { } /* Special case: unsafe option we do not want logged */ },
                { "unsafe_DisableSharedOpaqueEmptyDirectoryScrubbing", Logger.Log.ConfigUnsafeDisableSharedOpaqueEmptyDirectoryScrubbing },
                { "unsafe_ExistingDirectoryProbesAsEnumerations", Logger.Log.ConfigUnsafeExistingDirectoryProbesAsEnumerations },
                { "unsafe_ForceSkipDeps", Logger.Log.ForceSkipDependenciesEnabled },
                { "unsafe_GlobalPassthroughEnvVars",  loggingContext => { } /* Special case: unsafe option we do not want logged */ },
                { "unsafe_GlobalUntrackedScopes",  loggingContext => { } /* Special case: unsafe option we do not want logged */ },
                { "unsafe_IgnoreGetFinalPathNameByHandle", Logger.Log.ConfigIgnoreGetFinalPathNameByHandle },
                { "unsafe_IgnoreNonCreateFileReparsePoints", Logger.Log.ConfigIgnoreNonCreateFileReparsePoints },
                { "unsafe_IgnoreNtCreateFile", Logger.Log.ConfigUnsafeMonitorNtCreateFileOff },
                { "unsafe_IgnoreReparsePoints", Logger.Log.ConfigIgnoreReparsePoints },
                { "unsafe_IgnorePreloadedDlls", Logger.Log.ConfigIgnorePreloadedDlls },
                { "unsafe_IgnoreDynamicWritesOnAbsentProbes", Logger.Log.ConfigIgnoreDynamicWritesOnAbsentProbes },
                { "unsafe_IgnoreSetFileInformationByHandle", Logger.Log.ConfigIgnoreSetFileInformationByHandle },
                { "unsafe_IgnoreValidateExistingFileAccessesForOutputs", Logger.Log.ConfigIgnoreValidateExistingFileAccessesForOutputs },
                { "unsafe_IgnoreZwCreateOpenQueryFamily", Logger.Log.ConfigUnsafeMonitorZwCreateOpenQueryFileOff },
                { "unsafe_IgnoreZwOtherFileInformation", Logger.Log.ConfigIgnoreZwOtherFileInformation },
                { "unsafe_IgnoreZwRenameFileInformation", Logger.Log.ConfigIgnoreZwRenameFileInformation },
                { "unsafe_LazySymlinkCreation", Logger.Log.ConfigUnsafeLazySymlinkCreation },
                { "unsafe_MonitorFileAccesses", Logger.Log.ConfigUnsafeDisabledFileAccessMonitoring },
                { "unsafe_PreserveOutputs", Logger.Log.ConfigPreserveOutputs },
                { "unsafe_SourceFileCanBeInsideOutputDirectory", loggingContext => { } /* Special case: unsafe option we do not want logged */ },
                { "unsafe_UnexpectedFileAccessesAreErrors", Logger.Log.ConfigUnsafeUnexpectedFileAccessesAsWarnings },
                { "unsafe_IgnoreUndeclaredAccessesUnderSharedOpaques", Logger.Log.ConfigUnsafeIgnoreUndeclaredAccessesUnderSharedOpaques },
            };
        }

        [SuppressMessage("Microsoft.Interoperability", "CA1404:CallGetLastErrorImmediatelyAfterPInvoke", Justification = "Intentionally wrapping GetLastWin32Error")]
        private bool LogAndValidateConfiguration(LoggingContext loggingContext)
        {
            Contract.Ensures(Contract.Result<bool>() || loggingContext.ErrorWasLogged);

            LogExperimentalOptions(loggingContext, Configuration);

            var artificialCacheMissOptions = Configuration.Cache.ArtificialCacheMissOptions;
            if (artificialCacheMissOptions != null)
            {
                var effectiveMissRate = artificialCacheMissOptions.IsInverted
                    ? ushort.MaxValue - artificialCacheMissOptions.Rate
                    : artificialCacheMissOptions.Rate;
                var effectiveMissRateDouble = (double)effectiveMissRate / ushort.MaxValue;
                var regularSign = artificialCacheMissOptions.IsInverted ? "~" : string.Empty;
                var regular = I($"{regularSign}{(double)artificialCacheMissOptions.Rate / ushort.MaxValue}#{artificialCacheMissOptions.Seed}");
                var invertedSign = !artificialCacheMissOptions.IsInverted ? "~" : string.Empty;
                var inverted =
                    I($"{invertedSign}{(double)(ushort.MaxValue - artificialCacheMissOptions.Rate) / ushort.MaxValue}#{artificialCacheMissOptions.Seed}");

                Logger.Log.ConfigArtificialCacheMissOptions(loggingContext, effectiveMissRateDouble, regular, inverted);
            }

            if (Configuration.Schedule.LowPriority)
            {
                if (JobObject.OSSupportsNestedJobs)
                {
                    if (!JobObject.SetLimitInformationOnCurrentProcessJob(priorityClass: ProcessPriorityClass.BelowNormal))
                    {
                        Logger.Log.AssignProcessToJobObjectFailed(loggingContext, NativeWin32Exception.GetFormattedMessageForNativeErrorCode(System.Runtime.InteropServices.Marshal.GetLastWin32Error()));
                    }
                }
                else
                {
                    Logger.Log.CannotEnforceLowPriority(loggingContext);
                }
            }

            var unsafeOptionLoggers = CreateUnsafeOptionLoggers();

            // Log a warning for every unsafe option enabled by command line
            foreach (var opt in Configuration.CommandLineEnabledUnsafeOptions)
            {
                Contract.Assert(unsafeOptionLoggers.ContainsKey(opt));
                unsafeOptionLoggers[opt](loggingContext);
            }

            // Layout has been set up including directory creation. If journals are required,
            // we want to make sure that all key engine directories have a journal enabled.
            // This can catch configuration issues before the execution phase.
            if (!OperatingSystemHelper.IsUnixOS && !VerifyJournalAvailableForEngineVolumesIfRequired(loggingContext))
            {
                return false;
            }

            if (Configuration.FrontEnd.DebugScript() && Configuration.FrontEnd.ProfileScript())
            {
                Logger.Log.ConfigDebuggingAndProfilingCannotBeSpecifiedSimultaneously(loggingContext);
                return false;
            }

            if (!Configuration.FrontEnd.UseLegacyOfficeLogic())
            {
                // DScript V1 remnant warnings of deprecation
                if (Configuration.Packages != null)
                {
                    Logger.Log.WarnToNotUsePackagesButModules(loggingContext, Configuration.Layout.PrimaryConfigFile.ToString(Context.PathTable));
                }

                // DScript warnings for orphaned projects. Unfortunately given the current implementation lots of codebases have
                // to still set it to an empty array to function, so we'll need a multi-state deprecation.
                if (Configuration.Projects != null && Configuration.Projects.Count() > 0)
                {
                    var currentImplicitModuleName = Configuration.Layout.PrimaryConfigFile.GetParent(Context.PathTable).GetName(Context.PathTable).ToString(Context.StringTable);
                    Logger.Log.WarnToNotUseProjectsField(loggingContext, Configuration.Layout.PrimaryConfigFile.ToString(Context.PathTable), Names.ModuleConfigDsc, currentImplicitModuleName);
                }
            }

            return true;
        }

        private static void LogExperimentalOptions(LoggingContext loggingContext, IConfiguration config)
        {
            bool hasExperimentalOptions = false;
            var builder = new StringBuilder();

            Action<bool, string> appendConditionally = (condition, text) =>
            {
                if (!condition)
                {
                    return;
                }

                AppendWithSeparator(builder, text);
                hasExperimentalOptions = true;
            };

            appendConditionally(config.Sandbox.ForceReadOnlyForRequestedReadWrite, "Force read-only for requested read-write access");
            appendConditionally(config.Experiment.UseSubstTargetForCache, "Using subst target for the cache");

            if (hasExperimentalOptions)
            {
                Logger.Log.ConfigUsingExperimentalOptions(loggingContext, builder.ToString());
            }
        }

        private static void AppendWithSeparator(StringBuilder builder, string append)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(append);
        }

        /// <summary>
        /// If a change journal is needed in the current configuration, verifies that key engine directories (such as the object directory and cache directory)
        /// are on a journal-enabled volume. If false is returned, an error has been logged.
        /// This should be called after all engine directories have been created with <see cref="BuildXLEngine.CreateOutputDirectories" />.
        /// </summary>
        private bool VerifyJournalAvailableForEngineVolumesIfRequired(LoggingContext loggingContext)
        {
            bool allEnabled =
                VerifyJournalAvailableForPath(loggingContext, Configuration.Layout.SourceDirectory) &&
                VerifyJournalAvailableForPath(loggingContext, Configuration.Layout.ObjectDirectory) &&
                VerifyJournalAvailableForPath(loggingContext, Configuration.Layout.CacheDirectory) &&
                VerifyJournalAvailableForPath(loggingContext, Configuration.Layout.BuildEngineDirectory) &&
                VerifyJournalAvailableForPath(loggingContext, Configuration.Layout.EngineCacheDirectory);

            return allEnabled;
        }

        /// <summary>
        /// Verifies that a path is possibly on a volume with an enabled change journal. If false is returned, an error has been logged.
        /// </summary>
        private bool VerifyJournalAvailableForPath(LoggingContext loggingContext, AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            var translatedPath = m_translator?.Translate(Context.PathTable, path) ?? path;
            bool possiblyEnabled = IsJournalPossiblyAvailableForPath(Context.PathTable, translatedPath);
            if (!possiblyEnabled)
            {
                var drive = translatedPath.GetRoot(Context.PathTable).ToString(Context.PathTable).TrimEnd('\\');
                Logger.Log.JournalRequiredOnVolumeError(loggingContext, drive, GetConfigureJournalCommand(drive));
            }

            return possiblyEnabled;
        }

        /// <summary>
        /// Command to configure the journal for a given volume drive
        /// </summary>
        private static string GetConfigureJournalCommand(string drive)
        {
            // (hold up to ~5M records and costs ~512mb)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "fsutil") + " usn createjournal m=0x20000000 a=0x8000000 " + drive;
        }

        /// <summary>
        /// Low-risk check if a path is definitely on a volume with a disabled change journal. In some configurations, the journal is
        /// required and so we want to fail early if misconfiguration is apparent.
        /// </summary>
        private static bool IsJournalPossiblyAvailableForPath(PathTable pathTable, AbsolutePath path)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(path.IsValid);

            OpenFileResult result = FileUtilities.TryOpenDirectory(
                path.ToString(pathTable),
                FileShare.Delete | FileShare.ReadWrite,
                out SafeFileHandle handle);
            using (handle)
            {
                if (result.Succeeded)
                {
                    Possible<VersionedFileIdentity, Failure<VersionedFileIdentity.IdentityUnavailabilityReason>> possibleIdentity =
                        VersionedFileIdentity.TryQuery(handle);

                    if (!possibleIdentity.Succeeded &&
                        possibleIdentity.Failure.Content == VersionedFileIdentity.IdentityUnavailabilityReason.NotSupported)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private async Task<bool> TrySaveFileContentTable(LoggingContext loggingContext)
        {
            try
            {
                await FileContentTable.SaveAsync(Configuration.Layout.FileContentTableFile.ToString(Context.PathTable));
            }
            catch (BuildXLException ex)
            {
                Logger.Log.EngineErrorSavingFileContentTable(
                    loggingContext,
                    Configuration.Layout.FileContentTableFile.ToString(Context.PathTable),
                    ex.LogEventMessage);
                return false;
            }

            return true;
        }

        private TimedBlock<EmptyStruct, TEndObject> StartPhase<TEndObject>(
            LoggingContext loggingContext,
            EnginePhases phase,
            Action<LoggingContext> startAction,
            Action<LoggingContext, TEndObject> endAction,
            Func<TEndObject> endObjGetter)
            where TEndObject : IHasEndTime
        {
            var tb = TimedBlock<EmptyStruct, TEndObject>.Start(
                loggingContext,
                m_collector,
                phase.ToString(),
                (context, startObject) => startAction(context),
                default(EmptyStruct),
                (context, endObject) =>
                {
                    endAction(context, endObject);
                    EndPhase(phase);
                },
                endObjGetter);

            return tb;
        }

        private static void EndPhase(EnginePhases phase)
        {
            EngineEnvironmentSettings.TryLaunchDebuggerAfterPhase(phase);
        }

        /// <summary>
        /// Result of constructing the schedule
        /// </summary>
        /// <remarks>
        /// Needed to differentiate whether the graph was constructed or reloaded for sake of optionally calling a GC
        /// </remarks>
        private enum ConstructScheduleResult
        {
            None,
            Failure,
            ConstructedNewGraph,
            ReusedExistingGraph,
        }

        /// <summary>
        /// The steps needed to construct the Schedule.
        /// Note that <paramref name="engineSchedule" /> may be null even on successful return (depending on phase).
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode")]
        private ConstructScheduleResult ConstructSchedule(
            LoggingContext loggingContext,
            int maxDegreeOfParallelism,
            CacheInitializationTask cacheInitializationTask,
            JournalState journalState,
            EngineState engineState,
            out EngineSchedule engineSchedule,
            out RootFilter rootFilter)
        {
            Contract.Requires(maxDegreeOfParallelism > 0, "maxDegreeOfParallelism > 0");
            Contract.Ensures(
                Contract.Result<ConstructScheduleResult>() == ConstructScheduleResult.Failure ||
                !Configuration.Engine.Phase.HasFlag(EnginePhases.Schedule) ||
                Contract.ValueAtReturn(out engineSchedule) != null);
            Contract.Ensures(
                Contract.Result<ConstructScheduleResult>() == ConstructScheduleResult.Failure ||
                !Configuration.Engine.Phase.HasFlag(EnginePhases.Schedule) ||
                Contract.ValueAtReturn(out rootFilter) != null || Configuration.Distribution.BuildRole == DistributedBuildRoles.Worker);

            engineSchedule = null;
            rootFilter = null;
            bool enablePartialEvaluation = Configuration.FrontEnd.UsePartialEvaluation();

            bool reusedGraph = false;

            if (Configuration.Engine.Phase == EnginePhases.None)
            {
                EndPhase(EnginePhases.None);
                return ConstructScheduleResult.None;
            }

            var cachedGraphIdToLoad = Configuration.Cache.CachedGraphIdToLoad;
            GraphFingerprint graphFingerprint;
            if (!string.IsNullOrEmpty(cachedGraphIdToLoad))
            {
                var parsed = Fingerprint.TryParse(cachedGraphIdToLoad, out var fingerprint);
                Contract.Assert(parsed);

                // the id is already validated by argument parsing.
                var compositeFingerprint = CompositeGraphFingerprint.Zero;
                compositeFingerprint.OverallFingerprint = new ContentFingerprint(fingerprint);

                graphFingerprint = new GraphFingerprint(compositeFingerprint, compositeFingerprint);
            }
            else
            {
                // The fingerprint needs to include the values that get evaluated.
                EvaluationFilter partialEvaluationData = EvaluationFilter.Empty;
                if (enablePartialEvaluation &&
                    !EngineSchedule.TryGetEvaluationFilter(
                        loggingContext,
                        Context,
                        m_initialCommandLineConfiguration,
                        Configuration,
                        out partialEvaluationData))
                {
                    Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged during graph scheduling.");
                    return ConstructScheduleResult.Failure;
                }

                graphFingerprint = Configuration.Distribution.BuildRole != DistributedBuildRoles.Worker
                    ? GraphFingerprinter.TryComputeFingerprint(
                        loggingContext,
                        m_initialCommandLineConfiguration.Startup,
                        Configuration,
                        Context.PathTable,
                        // Get deserialized version of evaluation filter so that it no longer depends on path table.
                        partialEvaluationData.GetDeserializedFilter(),
                        FileContentTable,
                        m_commitId,
                        TestHooks)
                    : null;
            }

            GraphReuseResult reuseResult = null;

            if (Configuration.Engine.Phase.HasFlag(EnginePhases.Schedule)
                &&
                ((IsGraphCacheConsumptionAllowed() && graphFingerprint != null) ||
                 Configuration.Distribution.BuildRole == DistributedBuildRoles.Worker))
            {
                reuseResult = AttemptToReuseGraph(
                    loggingContext,
                    maxDegreeOfParallelism,
                    graphFingerprint,
                    m_initialCommandLineConfiguration.Startup.Properties,
                    cacheInitializationTask,
                    journalState,
                    engineState);

                if (TestHooks != null)
                {
                    TestHooks.GraphReuseResult = reuseResult;
                }

                if (reuseResult.IsFullReuse)
                {
                    engineSchedule = reuseResult.EngineSchedule;
                    reusedGraph = true;
                }

                if (engineSchedule == null)
                {
                    if (Configuration.Distribution.BuildRole == DistributedBuildRoles.Worker)
                    {
                        Logger.Log.DistributionWorkerCouldNotLoadGraph(loggingContext);
                        return ConstructScheduleResult.Failure;
                    }

                    var cacheConfiguration = Configuration.Cache;
                    if (HasExplicitlyLoadedGraph(cacheConfiguration))
                    {
                        if (cacheConfiguration.CachedGraphPathToLoad.IsValid)
                        {
                            Logger.Log.PipGraphByPathFailure(loggingContext, cacheConfiguration.CachedGraphPathToLoad.ToString(Context.PathTable));
                            return ConstructScheduleResult.Failure;
                        }

                        if (!string.IsNullOrEmpty(cacheConfiguration.CachedGraphIdToLoad))
                        {
                            Logger.Log.PipGraphByIdFailure(loggingContext, cacheConfiguration.CachedGraphIdToLoad);
                            return ConstructScheduleResult.Failure;
                        }

                        Contract.Assert(false, "Unhandled explicit graph load");
                    }
                }

                m_enginePerformanceInfo.CacheInitializationDurationMs = (long)cacheInitializationTask.InitializationTime.TotalMilliseconds;
            }

            Task<Possible<SymlinkDefinitions>> symlinkDefinitionsTask =
                SymlinkDefinitionFileProvider.TryPrepareSymlinkDefinitionsAsync(
                    loggingContext,
                    reuseResult,
                    Configuration,
                    m_masterService,
                    cacheInitializationTask,
                    Context,
                    m_tempCleaner);

            if (m_visualization != null)
            {
                m_visualization.Context.MakeAvailable(Context);
                m_visualization.Configuration.MakeAvailable(Configuration);
                m_visualization.LoggingContext.MakeAvailable(new LoggingContext(loggingContext, "Viewer"));
            }

            var phase = Configuration.Engine.Phase;
            try
            {
                if (engineSchedule == null)
                {
                    // We have established a graph cache miss, and may choose to save a graph for next time.
                    // We need an input tracker to accumulate all spec, config, assembly, etc. inputs which
                    // we use as part of constructing a new graph; the resulting assertions are
                    // used to validate re-use of this new graph on subsequent runs.
                    InputTracker inputTrackerForGraphConstruction;
                    if (graphFingerprint != null)
                    {
                        if (reuseResult?.InputChanges != null)
                        {
                            inputTrackerForGraphConstruction = InputTracker.ContinueExistingTrackerWithInputChanges(
                                loggingContext,
                                FileContentTable,
                                reuseResult.InputChanges,
                                graphFingerprint.ExactFingerprint);

                            // For instance, a spec is changed and it uses 'glob' instead of 'globR' and we should remove some tracked directories.
                            // Instead of modifying the set of directory fingerprints, let's retrack the directories by starting with an empty set.
                            inputTrackerForGraphConstruction.ClearDirectoryFingerprints();
                        }
                        else
                        {
                            inputTrackerForGraphConstruction = InputTracker.Create(
                                loggingContext,
                                FileContentTable,
                                journalState,
                                graphFingerprint.ExactFingerprint);
                        }
                    }
                    else
                    {
                        inputTrackerForGraphConstruction = InputTracker.CreateDisabledTracker(loggingContext);
                    }

                    var mountsTable = MountsTable.CreateAndRegister(loggingContext, Context, Configuration, m_initialCommandLineConfiguration.Startup.Properties);

                    using (var frontEndEngineAbstraction = new FrontEndEngineImplementation(
                        loggingContext,
                        Context.PathTable,
                        Configuration,
                        m_initialCommandLineConfiguration.Startup,
                        mountsTable,
                        inputTrackerForGraphConstruction,
                        m_snapshotCollector,
                        m_directoryTranslator,
                        () => FileContentTable,
                        GetTimerUpdatePeriodInMs(Configuration.Logging),
                        reuseResult?.IsPartialReuse == true))
                    {
                        PipGraph newlyEvaluatedGraph;
                        if (TestHooks?.FrontEndEngineAbstraction != null)
                        {
                            TestHooks.FrontEndEngineAbstraction.Value = frontEndEngineAbstraction;
                        }

                        Stopwatch sw = Stopwatch.StartNew();
                        // Create the evaluation filter
                        EvaluationFilter evaluationFilter;
                        if (enablePartialEvaluation)
                        {
                            if (!EngineSchedule.TryGetEvaluationFilter(
                                loggingContext,
                                Context,
                                m_initialCommandLineConfiguration,
                                Configuration,
                                out evaluationFilter))
                            {
                                Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged during graph scheduling.");
                                return ConstructScheduleResult.Failure;
                            }
                        }
                        else
                        {
                            evaluationFilter = EvaluationFilter.Empty;
                        }

                        if (!ConstructAndEvaluateGraph(
                            loggingContext,
                            frontEndEngineAbstraction,
                            cacheInitializationTask,
                            mountsTable,
                            evaluationFilter,
                            reuseResult,
                            out newlyEvaluatedGraph))
                        {
                            Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged during DScript graph construction.");
                            return ConstructScheduleResult.Failure;
                        }

                        m_enginePerformanceInfo.GraphConstructionDurationMs = sw.ElapsedMilliseconds;

                        if (Configuration.Engine.LogStatistics)
                        {
                            BuildXL.Tracing.Logger.Log.Statistic(
                                loggingContext,
                                new Statistic
                                {
                                    Name = "GraphConstruction.DurationMs",
                                    Value = m_enginePerformanceInfo.GraphConstructionDurationMs
                                });
                        }

                        if (m_snapshotCollector != null)
                        {
                            if (newlyEvaluatedGraph != null)
                            {
                                m_snapshotCollector.SetPipGraph(newlyEvaluatedGraph);
                            }

                            // Collect all mounts.
                            foreach (var mount in mountsTable.AllMounts)
                            {
                                m_snapshotCollector.RecordMount(mount);
                            }
                        }

                        if (!phase.HasFlag(EnginePhases.Schedule))
                        {
                            return reusedGraph ? ConstructScheduleResult.ReusedExistingGraph : ConstructScheduleResult.ConstructedNewGraph;
                        }

                        Contract.Assert(newlyEvaluatedGraph != null);

                        // TODO: Shouldn't need to force the cache until the execution phase; this can be fixed when BuildXLScheduler is constructed only in the Execute phase.
                        Possible<CacheInitializer> possibleCacheInitializer = cacheInitializationTask.GetAwaiter().GetResult();
                        if (!possibleCacheInitializer.Succeeded)
                        {
                            // StorageCacheStartupError has been logged by CacheInitializer
                            return ConstructScheduleResult.Failure;
                        }

                        var maybeSymlinkDefinitions = symlinkDefinitionsTask.GetAwaiter().GetResult();
                        if (!maybeSymlinkDefinitions.Succeeded)
                        {
                            Contract.Assert(
                                loggingContext.ErrorWasLogged,
                                "Failed to load symlink definitions file, but no error was logged.");
                            return ConstructScheduleResult.Failure;
                        }

                        CacheInitializer cacheInitializerForGraphConstruction = possibleCacheInitializer.Result;

                        engineSchedule = EngineSchedule.Create(
                            loggingContext,
                            context: Context,
                            cacheInitializer: cacheInitializerForGraphConstruction,
                            configuration: Configuration,
                            fileContentTable: FileContentTable,
                            pipGraph: newlyEvaluatedGraph,
                            journalState: journalState,
                            mountPathExpander: mountsTable.MountPathExpander,
                            directoryMembershipFingerprinterRules: new DirectoryMembershipFingerprinterRuleSet(Configuration, Context.StringTable),
                            performanceCollector: m_collector,
                            directoryTranslator: m_directoryTranslator,
                            maxDegreeOfParallelism: Configuration.FrontEnd.MaxFrontEndConcurrency(),
                            symlinkDefinitions: maybeSymlinkDefinitions.Result,
                            tempCleaner: m_tempCleaner,
                            buildEngineFingerprint: graphFingerprint?.ExactFingerprint.BuildEngineHash.ToString());

                        if (engineSchedule == null)
                        {
                            Contract.Assert(loggingContext.ErrorWasLogged, "An error should have been logged during graph scheduling.");
                            return ConstructScheduleResult.Failure;
                        }

                        // Dispose engine state if and only if a new engine schedule is constructed from scratch.
                        engineState?.Dispose();

                        Contract.Assert(
                            !EngineState.IsUsable(engineState),
                            "Previous engine state must be unusable if a new engine schedule is constructed from scratch.");

                        var envVarsImpactingBuild = frontEndEngineAbstraction.ComputeEnvironmentVariablesImpactingBuild();
                        var mountsImpactingBuild = frontEndEngineAbstraction.ComputeEffectiveMounts();
                        var availableEnvVars = frontEndEngineAbstraction.GetAllEnvironmentVariables();

                        // Disable caching graph in the unit tests which do not set the app deployment.
                        if (TestHooks == null || TestHooks.AppDeployment != null)
                        {
                            m_graphCacheContentCachePut = CacheEngineScheduleStateAsync(
                                loggingContext,
                                graphFingerprint,
                                engineSchedule,
                                inputTrackerForGraphConstruction,
                                envVarsImpactingBuild,
                                mountsImpactingBuild,
                                availableEnvVars,
                                mountsTable.MountsByName);
                        }

                        if (IsDistributedMaster)
                        {
                            // In a distributed build, we must synchronously wait for the graph to be placed in the content cache.
                            if (!m_graphCacheContentCachePut.GetAwaiter().GetResult())
                            {
                                Logger.Log.ErrorUnableToCacheGraphDistributedBuild(loggingContext);
                                return ConstructScheduleResult.Failure;
                            }
                        }
                    }
                }

                Contract.Assert(engineSchedule != null);

                // When constructing the graph above, we exit early if the schedule phase is not requested. But if we
                // didn't have to construct the engineSchedue, we wouldn't have early returned and still may not
                // have the schedule phase.
                if (!phase.HasFlag(EnginePhases.Schedule))
                {
                    return reusedGraph ? ConstructScheduleResult.ReusedExistingGraph : ConstructScheduleResult.ConstructedNewGraph;
                }

                // If the graph cache is a hit and engineState is not null and disposed, use the previous state to speed the scheduling process.
                SchedulerState previousSchedulerState = EngineState.IsUsable(engineState) ? engineState.SchedulerState : null;

                // Bail out if the build was canceled during graph construction
                if (engineSchedule.IsTerminating)
                {
                    return ConstructScheduleResult.Failure;
                }

                if (TestHooks != null)
                {
                    TestHooks.TempCleanerTempDirectory = m_tempCleaner.TempDirectory;
                }

                if (!engineSchedule.PrepareForBuild(
                    loggingContext,
                    m_initialCommandLineConfiguration,
                    Configuration,
                    previousSchedulerState,
                    ref rootFilter,
                    GetNonScrubbablePaths(),
                    m_enginePerformanceInfo))
                {
                    MakeScheduleInfoAvailableToViewer(engineSchedule);
                    Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged during graph scheduling.");
                    return ConstructScheduleResult.Failure;
                }
            }
            finally
            {
                if (phase.HasFlag(EnginePhases.Schedule))
                {
                    EndPhase(EnginePhases.Schedule);
                }
            }

            MakeScheduleInfoAvailableToViewer(engineSchedule);

            if (TestHooks?.Scheduler != null)
            {
                TestHooks.Scheduler.Value = engineSchedule.Scheduler;
            }

            // Need to thread through the process start time checking it from the currently running process is not
            // valid for server mode builds.
            engineSchedule.Scheduler.SetProcessStartTime(m_processStartTimeUtc);

            return reusedGraph ? ConstructScheduleResult.ReusedExistingGraph : ConstructScheduleResult.ConstructedNewGraph;
        }

        private IReadOnlyList<string> GetNonScrubbablePaths()
        {
            return EngineSchedule.GetNonScrubbablePaths(Context.PathTable, Configuration, FrontEndController.GetNonScrubbablePaths(), m_tempCleaner);
        }

        private void LogFrontEndStats(LoggingContext loggingContext)
        {
            if (Configuration.FrontEnd.LogStatistics)
            {
                Logger.Log.FrontEndStatsBanner(loggingContext);

                var scriptStats = FrontEndController.LogStatistics(Configuration.FrontEnd.ShowSlowestElementsStatistics, Configuration.FrontEnd.ShowLargestFilesStatistics);

                m_enginePerformanceInfo.FrontEndIOWeight = scriptStats.IOWeight;
            }
        }

        private void MakeScheduleInfoAvailableToViewer(EngineSchedule engineSchedule)
        {
            if (m_visualization != null && engineSchedule != null)
            {
                m_visualization.Scheduler.MakeAvailable(engineSchedule.Scheduler);
                m_visualization.PipGraph.MakeAvailable(engineSchedule.Scheduler.PipGraph);
                m_visualization.PipTable.MakeAvailable(engineSchedule.PipTable);
            }
        }

        private void WarnForVirusScan(LoggingContext loggingContext, ILayoutConfiguration layout)
        {
            DefenderChecker checker = DefenderChecker.Create();

            foreach (var path in new HashSet<AbsolutePath>(new[]
            {
                layout.CacheDirectory,
                layout.ObjectDirectory,
                layout.OutputDirectory,
                layout.SourceDirectory,
                layout.PrimaryConfigFile.GetParent(Context.PathTable),
            }))
            {
                string providedPath = path.ToString(Context.PathTable);

                // Translate to the original paths for the defender check
                string originalPath = m_translator != null ? m_translator.Translate(providedPath) : providedPath;

                if (checker.CheckStateForPath(originalPath) == MonitoringState.Enabled)
                {
                    Logger.Log.VirusScanEnabledForPath(loggingContext, providedPath);
                }
            }
        }

#region Logging stats about the build

        private static void LogObjectPoolStats<T>(LoggingContext loggingContext, string poolName, ObjectPool<T> pool) where T : class
        {
            Logger.Log.ObjectPoolStats(
                loggingContext,
                poolName,
                pool.ObjectsInPool,
                pool.UseCount,
                pool.FactoryCalls);
        }

        private static void LogObjectPoolStats<T>(LoggingContext loggingContext, string poolName, ArrayPool<T> pool)
        {
            Logger.Log.ObjectPoolStats(
                loggingContext,
                poolName,
                pool.ObjectsInPool,
                pool.UseCount,
                pool.FactoryCalls);
        }

        private void LogStats(LoggingContext loggingContext, EngineSchedule schedule, CacheInitializationTask cacheInitializationTask, ConstructScheduleResult constructScheduleResult)
        {
            if (!Configuration.Engine.LogStatistics)
            {
                return;
            }

            Logger.Log.StatsBanner(loggingContext);
            Logger.Log.GCStats(loggingContext, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

            LogObjectPoolStats(loggingContext, "StringBuilder", Pools.StringBuilderPool);
            LogObjectPoolStats(loggingContext, "List<string>", Pools.StringListPool);
            LogObjectPoolStats(loggingContext, "List<FileArtifact>", Pools.FileArtifactListPool);
            LogObjectPoolStats(loggingContext, "List<FileArtifactWithAttributes>", Pools.FileArtifactWithAttributesListPool);
            LogObjectPoolStats(loggingContext, "List<DirectoryArtifact>", Pools.DirectoryArtifactListPool);
            LogObjectPoolStats(loggingContext, "List<AbsolutePath>", Pools.AbsolutePathListPool);
            LogObjectPoolStats(loggingContext, "List<PathAtom>", Pools.PathAtomListPool);
            LogObjectPoolStats(loggingContext, "List<IdentifierAtom>", Pools.IdentifierAtomListPool);
            LogObjectPoolStats(loggingContext, "HashSet<FileArtifact>", Pools.FileArtifactSetPool);
            LogObjectPoolStats(loggingContext, "HashSet<FileArtifactWithAttributes>", Pools.FileArtifactWithAttributesSetPool);
            LogObjectPoolStats(loggingContext, "HashSet<DirectoryArtifact>", Pools.DirectoryArtifactSetPool);
            LogObjectPoolStats(loggingContext, "HashSet<AbsolutePath>", Pools.AbsolutePathSetPool);
            LogObjectPoolStats(loggingContext, "char[]", Pools.CharArrayPool);
            LogObjectPoolStats(loggingContext, "byte[]", Pools.ByteArrayPool);
            LogObjectPoolStats(loggingContext, "PipDataBuilder", Context.PipDataBuilderPool);

            if (schedule != null
                && constructScheduleResult != ConstructScheduleResult.Failure
                && constructScheduleResult != ConstructScheduleResult.None)
            {
                m_enginePerformanceInfo.SchedulerPerformanceInfo = schedule.LogStats(loggingContext);
            }

            foreach (var type in BuildXLWriterStats.Types.OrderByDescending(type => BuildXLWriterStats.GetBytes(type)))
            {
                var name = BuildXLWriterStats.GetName(type);
                var entries = BuildXLWriterStats.GetCount(type);
                var totalBytes = BuildXLWriterStats.GetBytes(type);
                Logger.Log.PipWriterStats(loggingContext, name, entries, totalBytes, entries == 0 ? 0 : (int)(totalBytes / entries));
            }

            PathTable pathTable = Context.PathTable;
            SymbolTable symbolTable = Context.SymbolTable;
            StringTable stringTable = Context.StringTable;
            TokenTextTable tokenTextTable = Context.TokenTextTable;

            Logger.Log.InterningStats(loggingContext, "PathTable", pathTable.Count, pathTable.SizeInBytes);
            Logger.Log.InterningStats(loggingContext, "SymbolTable", symbolTable.Count, symbolTable.SizeInBytes);
            Logger.Log.InterningStats(loggingContext, "StringTable", stringTable.Count, stringTable.SizeInBytes);
            Logger.Log.InterningStats(loggingContext, "TokenTextTable", tokenTextTable.Count, tokenTextTable.SizeInBytes);
            Logger.Log.ObjectCacheStats(loggingContext, "PathTable Expansion Cache", pathTable.CacheHits, pathTable.CacheMisses);
            Logger.Log.ObjectCacheStats(loggingContext, "SymbolTable Expansion Cache", symbolTable.CacheHits, symbolTable.CacheMisses);
            Logger.Log.ObjectCacheStats(loggingContext, "StringTable Expansion Cache", stringTable.CacheHits, stringTable.CacheMisses);
            Logger.Log.ObjectCacheStats(loggingContext, "TokenTextTable Expansion Cache", tokenTextTable.CacheHits, tokenTextTable.CacheMisses);

            Dictionary<string, long> tableSizeStats = new Dictionary<string, long>()
            {
                {"PathTableBytes", pathTable.SizeInBytes },
                {"SymbolTableBytes", symbolTable.SizeInBytes },
                {"StringTableBytes", stringTable.SizeInBytes },
                {"TokenTextTableBytes", tokenTextTable.SizeInBytes },
            };

            BuildXL.Tracing.Logger.Log.BulkStatistic(loggingContext, tableSizeStats);

            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            cacheInitializationTask?.GetAwaiter().GetResult().Then(
                cacheInitializer =>
                {
                    cacheInitializer.LogStats(loggingContext);
                    return Unit.Void;
                });
        }

#endregion

#region Graph Caching

        /// <summary>
        /// Determines whether the GraphCaching feature is allowed
        /// </summary>
        private bool IsGraphCacheConsumptionAllowed()
        {
            return
                Configuration.Cache.CacheGraph &&

                // If running a distributed build, coordinator node is responding for constructing and caching graph
                Configuration.Distribution.BuildRole != DistributedBuildRoles.Worker;
        }

        /// <summary>
        /// Determines whether the build has an explicitly specified graph to load
        /// </summary>
        private static bool HasExplicitlyLoadedGraph(ICacheConfiguration config)
        {
            Contract.Requires(config != null);

            return !string.IsNullOrEmpty(config.CachedGraphIdToLoad) ||
                   config.CachedGraphPathToLoad.IsValid;
        }

        /// <summary>
        /// Synchronously saves the schedule state to disk and returns a task that optionally puts it in the cache for use in a future run
        /// </summary>
        /// <remarks>
        /// Serializing the graph state to disk is synchronous since some of the serialization tasks are not thread-safe.
        /// TODO: a future optimization is to update serialization to be thread-safe and not block on it
        /// </remarks>
        private async Task<bool> CacheEngineScheduleStateAsync(
            LoggingContext loggingContext,
            GraphFingerprint graphFingerprint,
            EngineSchedule engineSchedule,
            InputTracker inputTracker,
            IReadOnlyDictionary<string, string> envVarsImpactingBuild,
            IReadOnlyDictionary<string, IMount> mountsImpactingBuild,
            IReadOnlyDictionary<string, string> availableEnvVars,
            IReadOnlyDictionary<string, IMount> availableMounts)
        {
            Contract.Requires(engineSchedule != null);
            Contract.Requires(inputTracker != null);

            GraphCacheSaveStatistics saveStats = default(GraphCacheSaveStatistics);
            using (var tb = TimedBlock<EmptyStruct, GraphCacheSaveStatistics>.Start(
                loggingContext,
                Statistics.GraphCacheSave,
                (context, emptyStruct) => Logger.Log.SerializingPipGraphStart(context),
                default(EmptyStruct),
                Logger.Log.SerializingPipGraphComplete,
                () => saveStats))
            {
                EngineSerializer serializer;

                try
                {
                    Stopwatch sw = Stopwatch.StartNew();

                    // Usually, we can use the graph fingerprint to create a correlation id that is going to be used to associate
                    // all related file that we are going to store; however, as part of some tests, no such graph fingerprint is available,
                    // and then we just create a unique id.
                    FileEnvelopeId correlationId = graphFingerprint == null
                        ? FileEnvelopeId.Create()
                        : new FileEnvelopeId(graphFingerprint.ExactFingerprint.OverallFingerprint.ToString());
                    serializer = CreateEngineSerializer(loggingContext, correlationId);

                    // Delete the PreviousInputs file before serializing anything because the files will not be in
                    // a usable state during serialization
                    FileUtilities.DeleteFile(serializer.PreviousInputsFinalized, tempDirectoryCleaner: m_tempCleaner);

                    // Task to save the previous inputs file to a temporary location while the graph is being serialized
                    //
                    // Adding the previous inputs file to the the list of file to serialize also means that the previous inputs file
                    // will be stored in the content cache as part of saving engine schedule to the content cache.
                    // The previous inputs file has to be stored to the content cache for the graph cache hit reason.
                    // Suppose that the previous input files was not stored to the content cache. Suppose further that
                    // BuildXL has a graph cache hit from the content cache (not from the engine cache), but since the previous inputs file
                    // is not stored to the content cache, the file is not materialized on disk. In the next build, BuildXL is unable
                    // to perform fast graph reuse check through the engine cache because that check relies on the previous inputs file.
                    // Thus, in that next build, BuildXL has to use the slow N-chain graph caching algorithm.
                    var previousInputsTask = serializer.SerializeToFileAsync(
                        GraphCacheFile.PreviousInputs,
                        writer => inputTracker.WriteToFile(
                            writer,
                            Context.PathTable,
                            envVarsImpactingBuild,
                            mountsImpactingBuild,
                            serializer.PreviousInputsJournalCheckpoint),
                        overrideName: EngineSerializer.PreviousInputsIntermediateFile);

                    // The RunningTimeTable task initializes asynchronously. It needs to complete before the StringTable's serialization starts to avoid races.
                    // The line below enforces that the lazy initialization has completed.
                    if (engineSchedule.Scheduler.RunningTimeTableTask != null)
                    {
                        Analysis.IgnoreResult(engineSchedule.Scheduler.RunningTimeTableTask.GetAwaiter().GetResult());
                    }

                    // We do not want to concurrently execute the serialization tasks and pips because pips can add new stuff to the PathTable, SymbolTable, and StringTable.
                    var success = engineSchedule.SaveToDiskAsync(serializer, Context).GetAwaiter().GetResult();
                    saveStats.SerializationMilliseconds = (int)sw.ElapsedMilliseconds;

                    // BuildXL should proceed immediately after we are done with saving files to disk. The rest can asynchronously happen with pip execution.
                    await Task.Yield();
                    m_executionLogGraphCopy = TryCreateHardlinksToScheduleFilesInSessionFolder(loggingContext, serializer);

                    // After await just above, the remainder is already scheduled in a thread pool so we do not need to wrap the statements below under a task.
                    if (success)
                    {
                        Context.EngineCounters.AddToCounter(EngineCounter.BytesSavedDueToCompression, serializer.BytesSavedDueToCompression);

                        if (Configuration.Cache.CacheGraph)
                        {
                            AsyncOut<PipGraphCacheDescriptor> cachedGraphDescriptor = new AsyncOut<PipGraphCacheDescriptor>();
                            AsyncOut<ContentFingerprint> identifierFingerprint = new AsyncOut<ContentFingerprint>();
                            success &= await engineSchedule.TrySaveToCacheAsync(
                                serializer,
                                inputTracker,
                                tb.LoggingContext,
                                m_rootTranslator,
                                envVarsImpactingBuild,
                                mountsImpactingBuild,
                                availableEnvVars,
                                availableMounts,
                                cachedGraphDescriptor,
                                identifierFingerprint);

                            if (success)
                            {
                                Logger.Log.PipGraphIdentfier(tb.LoggingContext, identifierFingerprint.Value.ToString());
                            }

                            if (success && Configuration.Distribution.BuildRole == DistributedBuildRoles.Master)
                            {
                                m_masterService.CachedGraphDescriptor = cachedGraphDescriptor.Value;
                            }
                        }

                        // Move the previous inputs file to its final location if graph serialization was successful
                        if ((await previousInputsTask).Success)
                        {
                            success &= serializer.FinalizePreviousInputsFile();
                            m_previousInputFilesCopy = TryCreateHardlinksToPreviousInputFilesInSessionFolder(loggingContext, serializer);
                        }
                    }

                    saveStats.SerializedFileSizeBytes = serializer.BytesSerialized;
                    saveStats.Success = success;
                }
                catch (BuildXLException ex)
                {
                    Logger.Log.FailedToSerializePipGraph(tb.LoggingContext, ex.LogEventMessage);
                    saveStats.Success = false;
                }

                return saveStats.Success;
            }
        }

        private async Task TryCreateHardlinksToScheduleFilesInSessionFolder(
            LoggingContext loggingContext,
            EngineSerializer serializer)
        {
            if (Configuration.Logging.LogExecution)
            {
                await EngineSchedule.DuplicateScheduleFiles(
                    loggingContext,
                    serializer,
                    Configuration.Logging.EngineCacheLogDirectory.ToString(Context.PathTable));
            }
        }

        private async Task TryCreateHardlinksToPreviousInputFilesInSessionFolder(
            LoggingContext loggingContext,
            EngineSerializer serializer)
        {
            if (Configuration.Logging.LogExecution)
            {
                await EngineSchedule.DuplicatePreviousInputFiles(
                    loggingContext,
                    serializer,
                    Configuration.Logging.EngineCacheLogDirectory.ToString(Context.PathTable));
            }
        }

        private EngineSerializer CreateEngineSerializer(LoggingContext loggingContext, FileEnvelopeId? correlationId = null)
        {
            var location = Configuration.Cache.CachedGraphPathToLoad;
            if (!location.IsValid)
            {
                location = Configuration.Layout.EngineCacheDirectory;
            }

            return new EngineSerializer(loggingContext, location.ToString(Context.PathTable), correlationId, Configuration.Engine.CompressGraphFiles);
        }

        private InputTracker.MatchResult CheckIfAvailableInputsToGraphMatchPreviousRun(
            LoggingContext loggingContext,
            EngineSerializer serializer,
            GraphFingerprint graphFingerprint,
            IBuildParameters availableEnvironmentVariables,
            MountsTable availableMounts,
            JournalState journalState,
            int maxDegreeOfParallelism)
        {
            return InputTracker.CheckIfAvailableInputsMatchPreviousRun(
                loggingContext,
                serializer,
                changeTrackingStatePath: serializer.PreviousInputsJournalCheckpoint,
                fileContentTable: FileContentTable,
                graphFingerprint: graphFingerprint,
                availableEnvironmentVariables: availableEnvironmentVariables,
                availableMounts: availableMounts,
                journalState: journalState,
                timeLimitForJournalScanning:
                    Configuration.Engine.ScanChangeJournalTimeLimitInSec < 0
                        ? (TimeSpan?)null
                        : TimeSpan.FromSeconds(Configuration.Engine.ScanChangeJournalTimeLimitInSec),
                maxDegreeOfParallelism: maxDegreeOfParallelism,
                configuration: Configuration,
                checkAllPossiblyChangedPaths: m_rememberAllChangedTrackedInputs);
        }

#endregion

        /// <summary>
        /// Gets the update and delay time for status timers
        /// </summary>
        public static int GetTimerUpdatePeriodInMs(ILoggingConfiguration loggingConfig) => loggingConfig == null || !loggingConfig.FancyConsole ? 5000 : 2000;
    }

    /// <summary>
    /// Class that represents the result of running the engine.
    /// </summary>
    public sealed class BuildXLEngineResult
    {
        /// <summary>
        /// Whether the engine is successfully ran.
        /// </summary>
        public readonly bool IsSuccess;

        /// <summary>
        /// Performance
        /// </summary>
        public readonly EnginePerformanceInfo EnginePerformanceInfo;

        /// <summary>
        /// The state that represents the engine context, carried between BuildXL builds if the BuildXL server is alive.
        /// </summary>
        public readonly EngineState EngineState;

        private BuildXLEngineResult(bool success, EngineState engineState, EnginePerformanceInfo enginePerformanceInfo)
        {
            IsSuccess = success;
            EngineState = engineState;
            EnginePerformanceInfo = enginePerformanceInfo;
        }

        /// <summary>
        /// Create a failed EngineResult with a given <see cref="EngineState" />
        /// </summary>
        /// <remarks>
        /// Although the previous engine state can be usable when failure occurrs, that engine state is simply thrown away.
        /// This avoids any complication when failure occurs after the previous engine state has been disposed, e.g., failure
        /// in saving file content table.
        /// </remarks>
        public static BuildXLEngineResult Failed(EngineState engineState)
        {
            // Dispose engine state so no one can use it.
            engineState?.Dispose();
            return Create(false, null, previousState: engineState, newState: null);
        }

        /// <summary>
        /// Create a EngineResult with a given <see cref="EngineState" /> and success,
        /// </summary>
        public static BuildXLEngineResult Create(bool success, EnginePerformanceInfo perfInfo, EngineState previousState, EngineState newState)
        {
#pragma warning disable SA1114 // Parameter list must follow declaration
            // There are three safe cases:
            Contract.Assert(EngineState.CorrectEngineStateTransition(previousState, newState, out var message), message);

            return new BuildXLEngineResult(success, newState, perfInfo);
#pragma warning restore SA1114 // Parameter list must follow declaration
        }
    }

    /// <summary>
    /// Performance info about the engine run
    /// </summary>
    public sealed class EnginePerformanceInfo
    {
        /// <nodoc/>
        public SchedulerPerformanceInfo SchedulerPerformanceInfo;

        /// <nodoc/>
        public long GraphConstructionDurationMs;

        /// <nodoc/>
        public long GraphCacheCheckDurationMs;

        /// <nodoc/>
        public bool GraphCacheCheckJournalEnabled;

        /// <nodoc/>
        public long GraphReloadDurationMs;

        /// <nodoc/>
        public long ScrubbingDurationMs;

        /// <nodoc/>
        public long SchedulerInitDurationMs;

        /// <nodoc/>
        public long ExecutePhaseDurationMs;

        /// <nodoc/>
        public long CacheInitializationDurationMs;

        /// <summary>
        /// Weight of IO time to CPU time during frontend evaluation
        /// </summary>
        public double FrontEndIOWeight;

        /// <nodoc/>
        public LimitingResourcePercentages LimitingResourcePercentages;
    }
}
