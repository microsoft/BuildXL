// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Distribution;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Processes;
using BuildXL.Processes.Sideband;
using BuildXL.Processes.VmCommandProxy;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Cache;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Performance;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Qualifier;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Core.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.ViewModel;
using Google.Protobuf;
using static BuildXL.Utilities.Core.FormattableStringEx;
using Logger = BuildXL.Engine.Tracing.Logger;
using SchedulerLogger = BuildXL.Scheduler.Tracing.Logger;

namespace BuildXL.Engine
{
    /// <summary>
    /// Class EngineSchedule.
    /// </summary>
    public sealed class EngineSchedule : IDisposable
    {
        private const string PreserveOutputsFileName = "PreserveOutputsInfo.txt";

        /// <nodoc />
        public readonly EngineContext Context;

        /// <summary>
        /// BuildXLScheduler that places pips on <see cref="SchedulingQueue" />. This provides access to the concrete underlying schedule if available.
        /// </summary>
        /// <remarks>
        /// This BuildXLScheduler may be null if the specified <see cref="EnginePhases" /> exclude scheduling / execution.
        /// </remarks>
        public readonly Scheduler.Scheduler Scheduler;

        /// <summary>
        /// Pip table that holds all pips.
        /// </summary>
        public PipTable PipTable { get; private set; }

        /// <summary>
        /// Implementation of <see cref="EngineCache" /> facets as provided by e.g. BuildCache.
        /// This cache instance is schedule-specific and so must be disposed at the end of the schedule; implementations may e.g.
        /// hold on to ('pin') content that is requested during the schedule for example.
        /// </summary>
        private readonly EngineCache m_cache;

        /// <summary>
        /// The pip queue.
        /// </summary>
        public readonly PipQueue SchedulingQueue;

        /// <summary>
        /// The index of the maximum serialized absolute path
        /// </summary>
        public readonly int MaxSerializedAbsolutePath;

        /// <summary>
        /// Mount path expander
        /// </summary>
        internal readonly MountPathExpander MountPathExpander;

        private readonly TempCleaner m_tempCleaner;

        private TimeSpan? m_schedulerStartTime;

        /// <summary>
        /// The minimum amount of time the build must run before the optimization data structures are serialized. This avoid overhead
        /// of serializing these data structures for extremely short builds.
        /// </summary>
        private static TimeSpan MinExecutionTimeForSerializingOptimizationDataStructures => EngineEnvironmentSettings.PostExecOptimizeThreshold;

        internal const int PipTableInitialBufferSize = 16384;

        private readonly ConfigFileState m_configFileState;

        /// <summary>
        /// File content table
        /// </summary>
        public readonly FileContentTable FileContentTable;

        /// <summary>
        /// Even on a 24 core machine, it seems that 1 thread is sufficient to keep up with the serialization tasks.
        /// </summary>
        internal static int PipTableMaxDegreeOfParallelismDuringConstruction => (Environment.ProcessorCount + 23) / 24;

        internal static int PipTableMaxDegreeOfParallelismDuringSerialization => Environment.ProcessorCount;

        private readonly int m_maxDegreeOfParallelism;

        private CancellableTimedAction m_updateStatusAction;

        /// <summary>
        /// Update status interval
        /// </summary>
        public const int UpdateStatusIntervalMs = 2_000;

        private EngineSchedule(
            EngineContext context,
            FileContentTable fileContentTable,
            Scheduler.Scheduler scheduler,
            EngineCache cache,
            PipTable pipTable,
            PipQueue schedulingQueue,
            MountPathExpander mountPathExpander,
            TempCleaner tempCleaner,
            ConfigFileState configFileState,
            int maxDegreeOfParallelism)
        {
            Contract.Requires(context != null);
            Contract.Requires(fileContentTable != null);
            Contract.Requires(scheduler != null);
            Contract.Requires(pipTable != null);
            Contract.Requires(schedulingQueue != null);
            Contract.Requires(mountPathExpander != null);
            Contract.Requires(configFileState != null);
            Contract.Requires(tempCleaner != null);

            MaxSerializedAbsolutePath = scheduler.PipGraph.MaxAbsolutePathIndex;
            Scheduler = scheduler;
            PipTable = pipTable;
            SchedulingQueue = schedulingQueue;
            Context = context;
            FileContentTable = fileContentTable;
            MountPathExpander = mountPathExpander;
            m_tempCleaner = tempCleaner;
            m_configFileState = configFileState;
            m_cache = cache;
            m_maxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        /// <summary>
        /// Creates a pip table suitable for backing a new, empty pip graph.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public static PipTable CreateEmptyPipTable(PipExecutionContext context)
        {
            Contract.Requires(context != null);
            return new PipTable(
                       context.PathTable,
                       context.SymbolTable,
                       initialBufferSize: PipTableInitialBufferSize,
                       maxDegreeOfParallelism: PipTableMaxDegreeOfParallelismDuringConstruction,
                       debug: false);
        }

        /// <summary>
        /// Creates an EngineSchedule for an immutable pip graph.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
             Justification = "The disposable objects ownership is handed over to the returned EngineSchedule that is responsible for disposing.")]
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static EngineSchedule Create(
            LoggingContext loggingContext,
            EngineContext context,
            CacheInitializer cacheInitializer,
            IConfiguration configuration,
            FileContentTable fileContentTable,
            PipGraph pipGraph,
            JournalState journalState,
            MountPathExpander mountPathExpander,
            DirectoryMembershipFingerprinterRuleSet directoryMembershipFingerprinterRules,
            PerformanceCollector performanceCollector,
            DirectoryTranslator directoryTranslator,
            int maxDegreeOfParallelism,
            TempCleaner tempCleaner,
            string buildEngineFingerprint,
            PipSpecificPropertiesConfig pipSpecificPropertiesConfig,
            IDetoursEventListener detoursListener = null)
        {
            Contract.Requires(context != null);
            Contract.Requires(configuration != null);
            Contract.Requires(cacheInitializer != null);
            Contract.Requires(pipGraph != null);

            var pipQueue = new PipQueue(loggingContext, configuration);

            if (configuration.Schedule.IncrementalScheduling &&
                (configuration.Distribution.BuildRole != DistributedBuildRoles.None ||
                 configuration.Schedule.ForceSkipDependencies != ForceSkipDependenciesMode.Disabled))
            {
                Logger.Log.ForceSkipDependenciesOrDistributedBuildOverrideIncrementalScheduling(loggingContext);
            }

            // We have a context which should be valid for the schedule. So, we can get a context-specific
            // cache for the schedule. Note that the resultant EngineSchedule will own this cache and dispose it later.
            EngineCache scheduleCache = cacheInitializer.CreateCacheForContext();

            var performanceDataFingerprint = PerformanceDataUtilities.ComputePerformanceDataFingerprint(
                loggingContext,
                context.PathTable,
                graphSemistableFingerprint: pipGraph.SemistableFingerprint,
                environmentFingerprint: configuration.Schedule.EnvironmentFingerprint);

            AsyncLazy<HistoricPerfDataTable> runtimeTable = Lazy.CreateAsync(() => TryLoadRunningTimeTable(
                loggingContext,
                context,
                configuration,
                Task.FromResult<Possible<EngineCache>>(scheduleCache),
                performanceDataFingerprint: performanceDataFingerprint));

            PipTwoPhaseCache twoPhaseCache = InitTwoPhaseCache(
                loggingContext,
                context,
                configuration,
                scheduleCache,
                performanceDataFingerprint: performanceDataFingerprint,
                pathExpander: mountPathExpander);

            var allowList = new FileAccessAllowlist(context);
            try
            {
                allowList.Initialize(configuration);
            }
            catch (BuildXLException ex)
            {
                Logger.Log.FailedToInitializeFileAccessAllowlist(loggingContext, ex.Message);
                return null;
            }

            ObservationReclassifier globalReclassifier = new ObservationReclassifier();

            var moduleConfigurations = new List<IModuleConfiguration> { configuration };
            moduleConfigurations.AddRange(configuration.ModulePolicies.Values);

            ConfigFileState configFileState = new ConfigFileState(
                                                    allowList,
                                                    configuration.Engine.DefaultFilter,
                                                    directoryMembershipFingerprinterRules,
                                                    moduleConfigurations,
                                                    globalReclassifier);

            PreserveOutputsInfo? previousOutputsSalt = PreparePreviousOutputsSalt(loggingContext, context.PathTable, configuration);
            if (!previousOutputsSalt.HasValue)
            {
                Contract.Assume(loggingContext.ErrorWasLogged, "Failed to prepare previous output salt, but no error was logged.");
                return null;
            }

            Scheduler.Scheduler scheduler;

            try
            {
                // If a detours listener was specified, propagate it as art of the scheduler test hook
                SchedulerTestHooks testHooks = null;
                if (detoursListener != null)
                {
                    testHooks = new SchedulerTestHooks { DetoursListener = detoursListener };
                }

                scheduler = new Scheduler.Scheduler(
                    pipGraph,
                    pipQueue,
                    context,
                    fileContentTable,
                    scheduleCache,
                    configuration,
                    tempCleaner: tempCleaner,
                    loggingContext: loggingContext,
                    runningTimeTable: runtimeTable,
                    fileAccessAllowlist: allowList,
                    directoryMembershipFingerprinterRules: directoryMembershipFingerprinterRules,
                    journalState: journalState,
                    performanceCollector: performanceCollector,
                    fingerprintSalt: configuration.Cache.CacheSalt,
                    previousInputsSalt: previousOutputsSalt.Value,
                    directoryTranslator: directoryTranslator,
                    pipTwoPhaseCache: twoPhaseCache,
                    buildEngineFingerprint: buildEngineFingerprint,
                    vmInitializer: VmInitializer.CreateFromEngine(
                        configuration.Layout.BuildEngineDirectory.ToString(context.PathTable),
                        configuration.Layout.ExternalSandboxedProcessDirectory.ToString(context.PathTable),
                        vmCommandProxyAlternate: EngineEnvironmentSettings.VmCommandProxyPath,
                        subst: configuration.Logging.SubstSource.IsValid && configuration.Logging.SubstTarget.IsValid
                            ? FileUtilities.GetSubstDriveAndPath(
                                configuration.Logging.SubstSource.ToString(context.PathTable),
                                configuration.Logging.SubstTarget.ToString(context.PathTable))
                            : default,
                        message => Logger.Log.StartInitializingVm(loggingContext, message),
                        message => Logger.Log.EndInitializingVm(loggingContext, message),
                        message => Logger.Log.InitializingVm(loggingContext, message)),
                    testHooks: testHooks,
                    pipSpecificPropertiesConfig: pipSpecificPropertiesConfig,
                    globalReclassificationRules: globalReclassifier);

            }
            catch (BuildXLException e)
            {
                Contract.Assert(
                    loggingContext.ErrorWasLogged,
                    $"Unable to construct schedule, but no error was logged. Exception caught: {e}");
                return null;
            }

            return Create(
                loggingContext,
                context,
                fileContentTable,
                pipGraph.PipTable,
                scheduler,
                scheduleCache,
                mountPathExpander,
                pipQueue,
                tempCleaner,
                configFileState,
                maxDegreeOfParallelism);
        }

        /// <summary>
        /// EngineSchedule creation given an already-initialized scheduler (new or loaded from disk).
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
             Justification = "The disposable objects ownership is handed over to the returned EngineScheudle that is responsible for disposing.")]
        private static EngineSchedule Create(
            LoggingContext loggingContext,
            EngineContext context,
            FileContentTable fileContentTable,
            PipTable pipTable,
            Scheduler.Scheduler scheduler,
            EngineCache cache,
            MountPathExpander mountPathExpander,
            PipQueue pipQueue,
            TempCleaner tempCleaner,
            ConfigFileState configFileState,
            int maxDegreeOfParallelism)
        {
            Contract.Requires(context != null);
            Contract.Requires(fileContentTable != null);
            Contract.Requires(pipTable != null);
            Contract.Requires(scheduler != null);
            Contract.Requires(mountPathExpander != null);
            Contract.Requires(tempCleaner != null);

            foreach (var moduleEntry in configFileState.ModuleConfigurations)
            {
                var fileAccessAllowlist = configFileState.FileAccessAllowlist.GetModuleAllowlist(moduleEntry.ModuleId);

                // Only log the global module and module's which actually have allowlist entries
                if (!moduleEntry.ModuleId.IsValid || fileAccessAllowlist != configFileState.FileAccessAllowlist)
                {
                    Logger.Log.FileAccessManifestSummary(
                        loggingContext,
                        moduleEntry.Name,
                        fileAccessAllowlist.CacheableEntryCount - configFileState.FileAccessAllowlist.CacheableEntryCount,
                        fileAccessAllowlist.UncacheableEntryCount - configFileState.FileAccessAllowlist.UncacheableEntryCount);
                }
            }

            return new EngineSchedule(
                       context,
                       fileContentTable,
                       scheduler,
                       cache,
                       pipTable,
                       pipQueue,
                       mountPathExpander,
                       tempCleaner,
                       configFileState,
                       maxDegreeOfParallelism);
        }

        /// <summary>
        ///  Returns path to the historic data file. Returns null if unable to resolve one - should be handled at the call site.
        /// </summary>
        private static string GetRunningTimeTableFilePath(PathTable pathTable, ILayoutConfiguration layout, LoggingContext loggingContext)
        {
            Contract.Requires(layout != null);
            Contract.Requires(loggingContext != null);
            try
            {
                return Path.Combine(layout.EngineCacheDirectory.ToString(pathTable), EngineSerializer.RunningTimeTableFile);
            }
            catch (Exception ex)
            {
                Logger.Log.FailedToResolveHistoricDataFileName(loggingContext, ex.GetLogEventMessage());
                return null;
            }
        }

        /// <summary>
        ///  Returns path to the historic metadata cache. Returns null if unable to resolve one - should be handled at the call site.
        /// </summary>
        private static string GetHistoricMetadataCacheDirectoryPath(PathTable pathTable, ILayoutConfiguration layout, LoggingContext loggingContext)
        {
            Contract.Requires(layout != null);
            Contract.Requires(loggingContext != null);
            try
            {
                return Path.Combine(layout.EngineCacheDirectory.ToString(pathTable), EngineSerializer.HistoricMetadataCacheLocation);
            }
            catch (Exception ex)
            {
                Logger.Log.FailedToResolveHistoricMetadataCacheFileName(loggingContext, ex.GetLogEventMessage());
                return null;
            }
        }

        private static async Task<Possible<EngineCache>> GetCacheForContext(CacheInitializationTask cacheInitializationTask)
        {
            var possibleCacheInitializer = await cacheInitializationTask;
            return possibleCacheInitializer.Then(cacheInitializer => cacheInitializer.CreateCacheForContext());
        }

        /// <summary>
        /// Attempts to load a historic metadata cache file.
        /// </summary>
        private static PipTwoPhaseCache InitTwoPhaseCache(
            LoggingContext loggingContext,
            EngineContext context,
            IConfiguration configuration,
            EngineCache cache,
            ContentFingerprint performanceDataFingerprint,
            PathExpander pathExpander)
        {
            if (configuration.Cache.HistoricMetadataCache != HistoricMetadataCacheMode.Disable)
            {
                var directoryPath = GetHistoricMetadataCacheDirectoryPath(context.PathTable, configuration.Layout, loggingContext);
                if (directoryPath != null)
                {
                    Logger.Log.HistoricMetadataCacheModeInvoked(loggingContext, configuration.Cache.HistoricMetadataCache.ToString());

                    if (configuration.Cache.HistoricMetadataCache == HistoricMetadataCacheMode.HashToHashAndMetadata)
                    {
                        return new HistoricMetadataCache(
                            loggingContext,
                            cache,
                            context,
                            pathExpander,
                            AbsolutePath.Create(context.PathTable, directoryPath),
                            prepareAsync: hmc => TryLoadHistoricMetadataCache(loggingContext, hmc, context, configuration, cache, performanceDataFingerprint),
                            logDirectoryLocation: configuration.Logging.HistoricMetadataCacheLogDirectory);
                    }
                    else if (configuration.Cache.HistoricMetadataCache == HistoricMetadataCacheMode.HashToHashOnly)
                    {
                        return new PipTwoPhaseCacheWithHashLookup(
                               loggingContext,
                               cache,
                               context,
                               pathExpander,
                               AbsolutePath.Create(context.PathTable, directoryPath),
                               prepareAsync: hmc => TryLoadHistoricMetadataCache(loggingContext, hmc, context, configuration, cache, performanceDataFingerprint),
                               logDirectoryLocation: configuration.Logging.HistoricMetadataCacheLogDirectory);
                    }

                    Contract.Assert(false, $"Cannot init HMD for {configuration.Cache.HistoricMetadataCache} value.");
                }
            }

            return new PipTwoPhaseCache(loggingContext, cache, context, pathExpander);
        }

        private bool ShouldSerializeOptimizationDataStructurePostExecution()
        {
            // Check if the build has run for required time in order to make serializing the optimizing data structures worthwhile.
            return m_schedulerStartTime != null &&
                (TimestampUtilities.Timestamp - m_schedulerStartTime.Value) > MinExecutionTimeForSerializingOptimizationDataStructures;
        }

        private static async Task TryLoadHistoricMetadataCache(
            LoggingContext loggingContext,
            PipTwoPhaseCacheWithHashLookup historicMetadataCache,
            EngineContext context,
            IConfiguration configuration,
            EngineCache cache,
            ContentFingerprint performanceDataFingerprint)
        {
            Contract.Requires(context != null);
            Contract.Requires(configuration != null);

            var location = historicMetadataCache.StoreLocation;
            bool fromCache = false;
            if (configuration.Schedule.ForceUseEngineInfoFromCache || !Directory.Exists(location) || !Directory.EnumerateFiles(location).Any())
            {
                using (historicMetadataCache.Counters.StartStopwatch(PipCachingCounter.HistoricRetrievalDuration))
                {
                    var result =
                        await cache.TryRetrieveHistoricMetadataCacheAsync(
                            loggingContext,
                            location,
                            context.PathTable,
                            configuration,
                            performanceDataFingerprint,
                            context.CancellationToken);
                    if (!result.Succeeded || !result.Result)
                    {
                        SchedulerLogger.Log.HistoricMetadataCacheTrace(loggingContext, I($"Could not load historic metadatacache data from cache"));
                        return;
                    }
                }

                fromCache = true;
                SchedulerLogger.Log.HistoricMetadataCacheTrace(loggingContext, I($"Loaded historic metadatacache data from cache"));
            }
            else
            {
                SchedulerLogger.Log.HistoricMetadataCacheTrace(
                    loggingContext,
                    I($"Historic metadatacache data found at: '{location}'. Skipping loading from cache."));
            }

            if (Directory.Exists(location))
            {
                SchedulerLogger.Log.HistoricMetadataCacheTrace(loggingContext, I($"Loading historic metadatacache data at: '{location}'."));

                historicMetadataCache.Counters.IncrementCounter(
                    fromCache ? PipCachingCounter.HistoricLoadedFromCache : PipCachingCounter.HistoricLoadedFromDisk);
            }
        }

        internal async Task<bool> TrySaveHistoricMetadataCache(
            LoggingContext loggingContext,
            EngineContext context,
            IConfiguration configuration)
        {
            if (configuration.Cache.HistoricMetadataCache != HistoricMetadataCacheMode.Disable)
            {
                var historicMetadataCache = Scheduler?.State?.Cache as PipTwoPhaseCacheWithHashLookup;
                
                if (!ShouldSerializeOptimizationDataStructurePostExecution() || historicMetadataCache == null)
                {
                    return true;
                }

                // Unblock the caller
                await Task.Yield();

                var location = historicMetadataCache.StoreLocation;

                if (historicMetadataCache.Valid)
                {
                    SchedulerLogger.Log.HistoricMetadataCacheTrace(
                        loggingContext,
                        I($"Saving historic metadata cache to path '{location ?? string.Empty}'"));
                }
                else
                {
                    // If the historic metadata cache was invalid, then there is nothing to
                    // save, so the save is "successful".
                    // The reason for invalidation should have already been logged
                    //
                    // This is different than the case where there is content that should be saved,
                    // and the save fails. In that case, we log an error as that could
                    // leave the historic metadata cache in a bad state for future runs.
                    return true;
                }

                using (historicMetadataCache.Counters.StartStopwatch(PipCachingCounter.HistoricSavingDuration))
                {
                    var performanceDataFingerprint = PerformanceDataUtilities.ComputePerformanceDataFingerprint(
                        loggingContext,
                        context.PathTable,
                        graphSemistableFingerprint: Scheduler.PipGraph.SemistableFingerprint,
                        environmentFingerprint: configuration.Schedule.EnvironmentFingerprint);

                    var storeResult =
                        await m_cache.TryStoreHistoricMetadataCacheAsync(
                            loggingContext,
                            location,
                            Context.PathTable,
                            configuration,
                            performanceDataFingerprint: performanceDataFingerprint);
                    if (!storeResult.Succeeded)
                    {
                        SchedulerLogger.Log.HistoricMetadataCacheSaveFailed(
                            loggingContext,
                            storeResult.Failure.DescribeIncludingInnerFailures());
                        return false;
                    }
                    else
                    {
                        historicMetadataCache.Counters.AddToCounter(PipCachingCounter.HistoricSavedSizeBytes, storeResult.Result);
                        SchedulerLogger.Log.HistoricMetadataCacheTrace(loggingContext, I($"Saving historic metadata cache to cache succeeded."));
                    }
                }
            }

            return true;
        }

        internal async Task<bool> TrySaveFingerprintStoreAsync(
            LoggingContext loggingContext,
            IConfiguration configuration)
        {
            // Save the fingerprint store to cache if the cache miss analysis is requested with remote mode.
            if (configuration.Logging.CacheMissAnalysisOption.ShouldPublishFingerprintStoreToCache && configuration.FingerprintStoreEnabled())
            {
                string storeKey = RuntimeCacheMissAnalyzer.GetStoreKeyForCurrentBuild(configuration.Logging.CacheMissAnalysisOption);

                if (storeKey == null)
                {
                    // We save fingerprintStore in cache only if the user passes a key to the /cachemiss option
                    // If there is no key entry, we do not save the fingerprintStore; but it is not failure either.
                    SchedulerLogger.Log.MissingKeyWhenSavingFingerprintStore(loggingContext, "no fingerprint store key information was given to: /cachemiss:<value>");
                    return false;
                }

                // Unblock the caller
                await Task.Yield();

                using (Context.EngineCounters.StartStopwatch(EngineCounter.FingerprintStoreSavingDuration))
                {
                    var storeResult = await m_cache.TrySaveFingerprintStoreAsync(
                        loggingContext,
                        configuration.Layout.FingerprintStoreDirectory, // This is where the original execution fingerprint store locates. Save this to cache.
                        Context.PathTable,
                        storeKey,
                        configuration);

                    if (!storeResult.Succeeded)
                    {
                        SchedulerLogger.Log.FingerprintStoreSavingFailed(
                            loggingContext,
                            storeResult.Failure.DescribeIncludingInnerFailures());
                        return false;
                    }
                    else
                    {
                        Context.EngineCounters.AddToCounter(EngineCounter.FingerprintStoreSavedSizeBytes, storeResult.Result);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Attempts to load a pip running time suggestion table saved in a previous build
        /// </summary>
        /// <remarks>
        /// Failure to load does not result in an error event, just possibly a warning.
        /// Note that this respects IEngineConfiguration.UseHistoricalPerformanceInfo and returns null if disabled.
        /// </remarks>
        private static async Task<HistoricPerfDataTable> TryLoadRunningTimeTable(
            LoggingContext loggingContext,
            EngineContext context,
            IConfiguration configuration,
            Task<Possible<EngineCache>> cacheTask,
            ContentFingerprint performanceDataFingerprint)
        {
            Contract.Requires(context != null);
            Contract.Requires(configuration != null);

            if (configuration.Schedule.UseHistoricalPerformanceInfo)
            {
                using (var pm = PerformanceMeasurement.StartWithoutStatistic(
                    loggingContext,
                    Logger.Log.StartLoadingHistoricPerfData,
                    Logger.Log.EndLoadingHistoricPerfData))
                {
                    bool fromCache = false;
                    var filePath = GetRunningTimeTableFilePath(context.PathTable, configuration.Layout, pm.LoggingContext);
                    if (filePath == null)
                    {
                        Contract.Assume(pm.LoggingContext.WarningWasLogged);
                        return null;
                    }

                    if (configuration.Schedule.ForceUseEngineInfoFromCache || !File.Exists(filePath))
                    {
                        SchedulerLogger.Log.HistoricPerfDataCacheTrace(
                            pm.LoggingContext,
                            I($"No historic perf data at: '{filePath}'. Attempting to load from cache."));

                        var possibleEngineCache = await cacheTask;
                        if (possibleEngineCache.Succeeded)
                        {
                            var cache = possibleEngineCache.Result;
                            Possible<bool> result;
                            using (context.EngineCounters.StartStopwatch(EngineCounter.HistoricPerfDataRetrievalDuration))
                            {
                                result =
                                    await cache.TryRetrieveRunningTimeTableAsync(
                                        pm.LoggingContext,
                                        filePath,
                                        context.PathTable,
                                        performanceDataFingerprint,
                                        context.CancellationToken);
                            }

                            if (!result.Succeeded || !result.Result)
                            {
                                SchedulerLogger.Log.HistoricPerfDataCacheTrace(pm.LoggingContext, I($"Could not load historic perf data from cache"));
                                return null;
                            }

                            fromCache = true;
                            context.EngineCounters.IncrementCounter(EngineCounter.HistoricPerfDataRetrievedFromCache);
                            SchedulerLogger.Log.HistoricPerfDataCacheTrace(pm.LoggingContext, I($"Loaded historic perf data from cache"));
                        }
                    }
                    else
                    {
                        SchedulerLogger.Log.HistoricPerfDataCacheTrace(
                            pm.LoggingContext,
                            I($"Historic perf data found at: '{filePath}'. Skipping loading from cache."));
                    }

                    if (File.Exists(filePath))
                    {
                        if (!fromCache)
                        {
                            context.EngineCounters.IncrementCounter(EngineCounter.HistoricPerfDataRetrievedFromDisk);
                        }

                        SchedulerLogger.Log.HistoricPerfDataCacheTrace(pm.LoggingContext, I($"Loading historic perf data at: '{filePath}'."));

                        try
                        {
                            HistoricPerfDataTable table = HistoricPerfDataTable.Load(pm.LoggingContext, filePath);
                            Logger.Log.HistoricPerfDataLoaded(pm.LoggingContext, table.Count);
                            context.EngineCounters.IncrementCounter(EngineCounter.HistoricPerfDataSuccessfullyLoaded);
                            return table;
                        }
                        catch (BuildXLException ex)
                        {
                            Logger.Log.LoadingHistoricPerfDataFailed(pm.LoggingContext, filePath, ex.LogEventMessage);
                            return null;
                        }
                    }

                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the pip filter for the build
        /// </summary>
        private bool TryGetPipFilter(
            LoggingContext loggingContext,
            BuildXLContext context,
            ICommandLineConfiguration commandLineConfiguration,
            IConfiguration configuration,
            out RootFilter rootFilter)
        {
            return TryGetPipFilter(
                loggingContext,
                context,
                commandLineConfiguration,
                configuration,
                MountPathExpander.TryGetRootByMountName,
                rootFilter: out rootFilter);
        }

        internal static void ScrubExtraneousFilesAndDirectories(
            [AllowNull] MountPathExpander mountPathExpander,
            Scheduler.Scheduler scheduler,
            LoggingContext loggingContext,
            IConfiguration configuration,
            IEnumerable<string> nonScrubbablePaths,
            ITempCleaner tempCleaner,
            RootFilter filter)
        {
            var pathsToScrub = new List<string>();
            HashSet<AbsolutePath> moduleScrubDirectories = scheduler.PipGraph.Modules
                .Where(m => m.Key.IsValid)
                .Select(moduleId => scheduler.PipGraph.PipTable.HydratePip(moduleId.Value.ToPipId(), PipQueryContext.SchedulerExecutePips) as Pips.Operations.ModulePip)
                .Where(m => m?.ScrubDirectories != null)
                .SelectMany(m => m.ScrubDirectories)
                .ToHashSet();

            pathsToScrub.AddRange(moduleScrubDirectories.Select(moduleScrubDirectory => moduleScrubDirectory.ToString(scheduler.Context.PathTable)));

            if (configuration.Engine.Scrub && mountPathExpander != null)
            {
                pathsToScrub.AddRange(mountPathExpander.GetScrubbableRoots().Select(p => p.ToString(scheduler.Context.PathTable)));
            }

            if (configuration.Engine.ScrubDirectories.Count > 0)
            {
                pathsToScrub.AddRange(configuration.Engine.ScrubDirectories.Select(p => p.ToString(scheduler.Context.PathTable)));
            }

            var sharedOpaqueDirectoriesToScrub = new List<DirectoryArtifact>();
            if (!configuration.Engine.AssumeCleanOutputs ?? true)
            {
                // We don't scrub composite shared directories since scrubbing the non-composite ones is enough to clean up all outputs
                sharedOpaqueDirectoriesToScrub.AddRange(scheduler.PipGraph.AllSealDirectories.Where(directoryArtifact =>
                    directoryArtifact.IsSharedOpaque &&
                    !scheduler.PipGraph.PipTable.IsSealDirectoryComposite(scheduler.PipGraph.GetSealedDirectoryNode(directoryArtifact).ToPipId())));
            }

            HashSet<string> nonDeletableDirectories = null;

            if (pathsToScrub.Count > 0 || sharedOpaqueDirectoriesToScrub.Count > 0)
            {
                // All directories that can contain outputs should not be deleted. One reason for this is
                // some pips may probe such directories, and such a probe is recorded by incremental scheduling state.
                // If that directory is deleted, then incremental scheduling will mark those pips dirty, although it is not
                // the user who deleted the directory.
                //
                // Similar situation happens with temporary directories. Pip specifies Out\D1\D2 as a temp directory. 
                // Pip probes Out\D1 during execution. After execution, our temp cleaner deletes Out\D1\D2, but (of course) not Out\D1. 
                // In the next build, the scrubber deletes Out\D1 because it is not in the graph, and is not an output directory. 
                // Unfortunately, the probe of Out\D1 is recorded in incremental scheduling state, and thus the deletion makes the pip dirty.
                // Some customers' no-op (dev) builds can go from 1s to 5-6s because of this invalidation.
                //
                // Another alternative is to make incremental scheduling use FileSystemView for existence checking
                // during journal scanning. This alternative requires more plumbing; see Task 1241786.
                nonDeletableDirectories = scheduler.PipGraph.AllDirectoriesContainingOutputs()
                    .Concat(scheduler.PipGraph.AllParentsOfTemporaryPaths())
                    .Concat(scheduler.PipGraph.AllSealDirectories.Select(sd => sd.Path))
                    .Select(d => d.ToString(scheduler.Context.PathTable))
                    .ToHashSet(OperatingSystemHelper.PathComparer);
            }

            var scrubber = new DirectoryScrubber(
                cancellationToken: scheduler.Context.CancellationToken,
                loggingContext: loggingContext,
                loggingConfiguration: configuration.Logging,
                maxDegreeParallelism: Environment.ProcessorCount,
                tempDirectoryCleaner: tempCleaner);

            var sidebandExaminer = new SidebandExaminer(loggingContext, scheduler, configuration, filter);
            var computeExtraneousSidebandFiles = true; // TODO: no need to do it if we got graph cache hit
            var sidebandState = sidebandExaminer.Examine(computeExtraneousSidebandFiles);
            scheduler.SetSidebandState(sidebandState);

            if (sidebandState.ShouldPostponeDeletion)
            {
                Logger.Log.PostponingDeletionOfSharedOpaqueOutputs(loggingContext);

                // must still delete all files recorded in all extraneous sideband files though
                if (sidebandState.ExtraneousSidebandFiles.Count > 0)
                {
                    Logger.Log.DeletingOutputsFromExtraneousSidebandFilesStarted(loggingContext);
                    scrubber.DeleteFiles(sidebandExaminer.TryReadAllRecordedWrites(sidebandState.ExtraneousSidebandFiles));
                    scrubber.DeleteFiles(sidebandState.ExtraneousSidebandFiles, logDeletedFiles: false);
                }
            }
            else
            {
                var sharedOpaqueSidebandDirectory = configuration.Layout.SharedOpaqueSidebandDirectory.ToString(scheduler.Context.PathTable);
                var sharedOpaqueSidebandFiles = SidebandWriterHelper.FindAllProcessPipSidebandFiles(sharedOpaqueSidebandDirectory);
                var distinctRecordedWrites = sidebandExaminer.TryReadAllRecordedWrites(sharedOpaqueSidebandFiles);

                if (distinctRecordedWrites.Any())
                {
                    Logger.Log.DeletingOutputsFromSharedOpaqueSidebandFilesStarted(loggingContext);
                    scrubber.DeleteFiles(distinctRecordedWrites);

                    Logger.Log.DeletingSharedOpaqueSidebandFilesStarted(loggingContext);
                    scrubber.DeleteFiles(sharedOpaqueSidebandFiles, logDeletedFiles: false);
                }
            }

            if (pathsToScrub.Count > 0)
            {
                Logger.Log.ScrubbingStarted(loggingContext);
                scrubber.RemoveExtraneousFilesAndDirectories(
                    isPathInBuild: path => scheduler.PipGraph.IsPathInBuildOrShouldNotBeScrubbed(AbsolutePath.Create(scheduler.Context.PathTable, path)),
                    pathsToScrub: pathsToScrub,
                    blockedPaths: nonScrubbablePaths,
                    nonDeletableRootDirectories: nonDeletableDirectories,
                    mountPathExpander: mountPathExpander,
                    statisticIdentifier: "Scrubbing");
            }

            // Shared opaque content is always deleted, regardless of what configuration.Engine.Scrub says.
            //
            // The shared opaque content is deleted either now (eagerly), or, when certain conditions are met 
            // (see SidebandExaminationResult.ShouldPostponeDeletion), lazily right before the corresponding pips are executed
            // (see SandboxedProcessPipExecutor.PrepareOutputsAsync).
            //
            // We need to delete shared opaques because otherwise hardlinks can't be used (content will remain readonly, which will
            // block the next build from modifying them) and to increase consistency in tool behavior and make them more agnostic to
            // the state of the disk that past builds could have produced.
            // TODO: This nuclear deletion is a temporary measure to deal with the fact that shared opaque directory outputs are not known
            // in advance. We need a better solution.
            // TODO: we can consider conflating these two scrubbing passes (first one is optional) into one call to DirectoryScrubber to
            // avoid enumerating the disk twice. But this involves some refactoring of the scrubber, where each path to scrub needs its own
            // isPathInBuild, mountPathExpander being on/off, etc. Revisit if two passes become a perf problem.
            if (!sidebandState.ShouldPostponeDeletion && sharedOpaqueDirectoriesToScrub.Count > 0)
            {
                // Add the set of exclusion to the collection of non-scrubbable paths: it is safe to not scrub under those since there are no
                // shared opaque outputs produced under exclusions by construction
                nonScrubbablePaths = nonScrubbablePaths.Union(
                    scheduler.PipGraph.OutputDirectoryExclusions.UnsafeGetList().Select(exclusion => exclusion.ToString(scheduler.Context.PathTable)));

                // The condition to delete a file under a shared opaque is more strict than for regular scrubbing: only files that have a specific
                // timestamp (which marks files as being shared opaque outputs) are deleted.
                Logger.Log.ScrubbingSharedOpaquesStarted(loggingContext);
                scrubber.RemoveExtraneousFilesAndDirectories(
                    // Everything that is not an output under a shared opaque is considered part of the build.
                    isPathInBuild: path =>
                        !SharedOpaqueOutputHelper.IsSharedOpaqueOutput(path) ||
                        ShouldRemoveEmptyDirectories(configuration, path),
                    pathsToScrub: sharedOpaqueDirectoriesToScrub.Select(directory => directory.Path.ToString(scheduler.Context.PathTable)),
                    blockedPaths: nonScrubbablePaths,
                    nonDeletableRootDirectories: nonDeletableDirectories,
                    // Mounts don't need to be scrubbable for this operation to take place.
                    mountPathExpander: null,
                    statisticIdentifier: "SharedOpaqueScrubbing");
            }
        }

        private static bool ShouldRemoveEmptyDirectories(IConfiguration configuration, string path)
        {
            // EnumerateFileSystemEntries is known to be slow, but is used anyways because of the expected use-case.
            return configuration.Schedule.UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any();
        }

        internal static IReadOnlyList<string> GetNonScrubbablePaths(
            PathTable pathTable,
            IConfiguration configuration,
            IEnumerable<string> extraNonScrubbablePaths,
            [AllowNull] ITempCleaner tempCleaner)
        {
            var nonScrubbablePaths = new List<string>(new[]
            {
                // Don't scrub the object directory lock file
                FolderLock.GetLockPath(configuration.Layout.ObjectDirectory.ToString(pathTable))
            });

            if (tempCleaner != null)
            {
                // Don't scrub the temp directory used for file move-delete
                // TempCleaner is responsible for cleaning this
                nonScrubbablePaths.Add(tempCleaner.TempDirectory);
            }

            AddToNonScrubbableAbsolutePaths(new[]
            {
                // Don't scrub the engine cache in the object directory
                configuration.Layout.EngineCacheDirectory,

                // Don't scrub the cache directory
                configuration.Layout.CacheDirectory,

                // Don't scrub log directories.
                configuration.Logging.LogsDirectory,
                configuration.Logging.RedirectedLogsDirectory
            });

            if (OperatingSystemHelper.IsUnixOS)
            {
                // Don't scrub the .NET Core lock file when running the CoreCLR on Unix even if its parent directory is specified as scrubbable.
                // Some build tools use the '/tmp' folder as temporary file location (e.g. xcodebuild, clang, etc.) for dumping state and reports.
                // Unfortunately scrubbing the dotnet state files can lead to a misbehaving CoreCLR in subsequent or parallel runs where several
                // dotnet invocations happen, so lets avoid scrubbing that folder explicitly!
                nonScrubbablePaths.AddRange(new[]
                {
                    "/tmp/.dotnet/",
                    "/private/tmp/.dotnet/",
                });
            }

            // Don't scrub any of the paths flagged as non-scrubbable
            nonScrubbablePaths.AddRange(extraNonScrubbablePaths);

            void AddToNonScrubbableAbsolutePaths(AbsolutePath[] paths)
            {
                foreach (AbsolutePath path in paths)
                {
                    if (path.IsValid)
                    {
                        nonScrubbablePaths.Add(path.ToString(pathTable));
                    }
                }
            }

            return nonScrubbablePaths;
        }

        private void ScrubExtraneousFilesAndDirectories(
            LoggingContext loggingContext,
            IConfiguration configuration,
            IEnumerable<string> nonScrubbablePaths,
            RootFilter filter)
        {
            ScrubExtraneousFilesAndDirectories(
                MountPathExpander,
                Scheduler,
                loggingContext,
                configuration,
                nonScrubbablePaths,
                m_tempCleaner,
                filter);
        }

        /// <summary>
        /// Prepares scheduler for building.
        /// </summary>
        public bool PrepareForBuild(
            LoggingContext loggingContext,
            ICommandLineConfiguration commandLineConfiguration,
            IConfiguration configuration,
            SchedulerState schedulerState,
            ref RootFilter filter,
            IReadOnlyList<string> nonScrubbablePaths,
            EnginePerformanceInfo enginePerformanceInfo)
        {
            Contract.Requires(!HasFailed, "Build has already failed. Engine should have bailed out");

            if (!configuration.Engine.Phase.HasFlag(EnginePhases.Schedule))
            {
                return true;
            }

            if (IsTerminating)
            {
                return false;
            }

            // The filter may or may not have already been computed depending on whether there was a graph hit or not.
            if (filter == null && !TryGetPipFilter(loggingContext, Context, commandLineConfiguration, configuration, out filter))
            {
                return false;
            }

            LogPipFilter(loggingContext, filter);
            Logger.Log.FilterDetails(loggingContext, filter.GetStatistics());

            // Do scrub before init (Scheduler.Init() and Scheduler.InitForWorker()) because init captures and tracks
            // filesystem state used later by the scheduler. Scrubbing modifies the filesystem and would make the state that init captures
            // incorrect if they were to be interleaved.
            var scrubbingStopwatch = System.Diagnostics.Stopwatch.StartNew();
            ScrubExtraneousFilesAndDirectories(loggingContext, configuration, nonScrubbablePaths, filter);
            enginePerformanceInfo.ScrubbingDurationMs = scrubbingStopwatch.ElapsedMilliseconds;

            if (configuration.Distribution.BuildRole == DistributedBuildRoles.Worker)
            {
                return Scheduler.InitForWorker(loggingContext);
            }

            var initStopwatch = System.Diagnostics.Stopwatch.StartNew();
            bool initResult = Scheduler.InitForOrchestrator(loggingContext, filter, schedulerState);
            enginePerformanceInfo.SchedulerInitDurationMs = initStopwatch.ElapsedMilliseconds;

            return initResult;
        }

        /// <summary>
        /// Gets the data used to do partial evaluation. This must be included in the pip fingerprint
        /// </summary>
        public static bool TryGetEvaluationFilter(
            LoggingContext loggingContext,
            BuildXLContext context,
            ICommandLineConfiguration commandLineConfiguration,
            IConfiguration configuration,
            out EvaluationFilter evaluationFilter)
        {
            // The values to run are based off the filter, which we can't actually compute yet because we don't have
            // valid mount info. But the evaluation filter doesn't require any knowledge of mounts so we just give a dummy
            // resolver function. This will produce a filter that won't actually be able to be applied to pips but it
            // will have the correct values to resolve. Because of this the filter is thrown away and the actual pip
            // filter is created later in engine execution.
            if (TryGetPipFilter(
                loggingContext: loggingContext,
                context: context,
                commandLineConfiguration: commandLineConfiguration,
                configuration: configuration,
                mountResolver: DummyPathResolver,
                rootFilter: out RootFilter rootFilter))
            {
                evaluationFilter = rootFilter.GetEvaluationFilter(context.SymbolTable, context.PathTable);
                return true;
            }

            // Error will be logged when parsing the PipFilter.
            evaluationFilter = null;
            return false;
        }

        private static bool DummyPathResolver(string s, out AbsolutePath path)
        {
            // The dummy path returned must be valid
            path = new AbsolutePath(1);
            return true;
        }

        /// <summary>
        /// Logs the pip filter to the console
        /// </summary>
        public static void LogPipFilter(LoggingContext loggingContext, RootFilter rootFilter)
        {
            if (!rootFilter.IsEmpty)
            {
                Logger.Log.ConfigUsingPipFilter(loggingContext, rootFilter.FilterExpression);
            }
        }

        /// <summary>
        /// Gets the pip filter for the build
        /// </summary>
        public static bool TryGetPipFilter(
            LoggingContext loggingContext,
            BuildXLContext context,
            ICommandLineConfiguration commandLineConfiguration,
            IConfiguration configuration,
            FilterParser.TryGetPathByMountName mountResolver,
            out RootFilter rootFilter)
        {
            rootFilter = null;
            FilterParserError error;

            var canonicalize = configuration.Schedule.CanonicalizeFilterOutputs;
            var filterUnParsed = commandLineConfiguration.Filter;
            var defaultFilter = configuration.Engine.DefaultFilter;
            var implicitFilters = commandLineConfiguration.Startup.ImplicitFilters;

            if (filterUnParsed != null)
            {
                // A Pip Filter specified on the command line takes highest precedence
                if (implicitFilters != null && implicitFilters.Count > 0)
                {
                    // A command line /filter: option cannot be used conjunction with free form implicit filters
                    Logger.Log.ConfigFilterAndPathImplicitNotSupported(loggingContext, string.Join("; ", implicitFilters));
                    return false;
                }

                // A user may explicitly set it to empty to blank out what would normally fall back on the
                // default filter from the config file
                if (filterUnParsed.Length == 0)
                {
                    rootFilter = new RootFilter(new EmptyFilter());
                    return true;
                }

                // Otherwise we parse the actual filter
                FilterParser parser = new FilterParser(context, mountResolver, filterUnParsed, canonicalize: canonicalize);
                if (!parser.TryParse(out rootFilter, out error))
                {
                    Logger.Log.ConfigFailedParsingCommandLinePipFilter(
                        loggingContext,
                        filterUnParsed,
                        error.Position,
                        error.FormatFilterPointingToPosition(filterUnParsed),
                        error.Message);
                    return false;
                }
            }
            else if (implicitFilters != null && implicitFilters.Count > 0)
            {
                // Next, any values specified on the command line will trump the default filter
                StringBuilder sb = new StringBuilder();
                foreach (var implicitFilter in implicitFilters)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(" or ");
                    }

                    if (implicitFilter.StartsWith("*", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendFormat(@"output='{0}' or spec='{0}'", implicitFilter);
                    }
                    else
                    {
                        // If the implicit filter doesn't start with a '*' wildcard, we specify a wildcard
                        // with a directory separator to make sure the filename prefix is not wildcarded, only the
                        // directory containing the file
                        sb.AppendFormat(@"output='*{0}{1}' or spec='*{0}{1}' or tag='{1}'", Path.DirectorySeparatorChar, implicitFilter);
                    }

                    if (FilterParser.TryParsePipId(implicitFilter, out var pipId))
                    {
                        sb.AppendFormat(I($" or id='{implicitFilter}'"));
                    }
                }

                FilterParser parser = new FilterParser(context, mountResolver, sb.ToString(), canonicalize: canonicalize);

                if (!parser.TryParse(out rootFilter, out error))
                {
                    Logger.Log.ConfigFailedParsingCommandLinePipFilter(
                        loggingContext,
                        filterUnParsed,
                        error.Position,
                        error.FormatFilterPointingToPosition(filterUnParsed),
                        error.Message);
                    return false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(defaultFilter))
            {
                // Then fall back to the default filter
                FilterParser parser = new FilterParser(context, mountResolver, defaultFilter, canonicalize: canonicalize);
                RootFilter parsedFilter;
                if (!parser.TryParse(out parsedFilter, out error))
                {
                    Logger.Log.ConfigFailedParsingDefaultPipFilter(
                        loggingContext,
                        defaultFilter,
                        error.Position,
                        error.FormatFilterPointingToPosition(defaultFilter),
                        error.Message);
                    return false;
                }

                // Partially evaluating the graph when the filter comes from a default filter would
                // introduce a dependency that makes graph caching more complicated. Basically every input to the
                // graph fingerprint must be known up front before starting any evaluation. But the default filter is
                // defined in the config file which we must evaluate before accessing. So allowing the default filter
                // to impact the graph fingerprint would break this assumption and require partial evaluation before
                // checking for a cached graph. Doing so is possible but would add quite a bit of complexity.
                //
                // In order to avoid the default filter from impacting the graph fingerprint, we simply need to disallow
                // it from providing any value short circuiting information. In other words: any time the default filter
                // is used, the graph will be fully evaluated.
                // NOTE: Any change here will require changes in TryGetValuesImpactingGraphFingerprint() which assumes
                // the default fingerprint will not impact the graph fingerprint.
                parsedFilter.DisableValueShortCircuiting();

                rootFilter = parsedFilter;
            }
            else
            {
                // And finally create an empty filter if nothing was specified in the config or on the command line
                rootFilter = new RootFilter(new EmptyFilter());
            }

            return true;
        }

        internal static PreserveOutputsInfo? PreparePreviousOutputsSalt(LoggingContext loggingContext, PathTable pathTable, IConfiguration config)
        {
            if (config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs != PreserveOutputsMode.Disabled)
            {
                // PreserveOutputs isn't allowed in distributed builds primarily because we'd need to think through
                // synchronizing the salt file across machines. The feature is targeted at dev machine builds which are
                // single machine anyway, so for the time being we do not allow the 2 in combination.
                if (config.Distribution.BuildRole != DistributedBuildRoles.None)
                {
                    Logger.Log.PreserveOutputsNotAllowedInDistributedBuild(loggingContext);
                    return null;
                }

                var path = config.Layout.CacheDirectory.Combine(pathTable, PreserveOutputsFileName);
                string preserveOutputsSalt = path.ToString(pathTable);

                string guid;
                try
                {
                    if (!File.Exists(preserveOutputsSalt) || config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputs == PreserveOutputsMode.Reset)
                    {
                        guid = Guid.NewGuid().ToString();
                        File.WriteAllText(preserveOutputsSalt, guid);
                        Logger.Log.PreserveOutputsWithNewSalt(loggingContext, guid);
                    }
                    else
                    {
                        guid = File.ReadAllText(preserveOutputsSalt);
                        Logger.Log.PreserveOutputsWithExistingSalt(loggingContext, guid);
                    }
                }
                catch (IOException ex)
                {
                    Logger.Log.PreserveOutputsFailedToInitializeSalt(loggingContext, ex.Message);
                    return null;
                }

                return new PreserveOutputsInfo(ContentHashingUtilities.HashString(guid), config.Sandbox.UnsafeSandboxConfiguration.PreserveOutputsTrustLevel);
            }

            return UnsafeOptions.PreserveOutputsNotUsed;
        }

        /// <summary>
        /// Handles reporting end of build status and persisting some state. Returns 'false' on failure (error already logged).
        /// </summary>
        public async Task<bool> ProcessPostExecutionTasksAsync(
            LoggingContext loggingContext,
            EngineContext context,
            IConfiguration configuration)
        {
            using (Context.EngineCounters.StartStopwatch(EngineCounter.ProcessPostExecutionTasksDuration))
            {
                if (!Scheduler.IsInitialized)
                {
                    // If scheduler is not initialized, graph and fingerprints should not be exported and scheduling state should not be saved.
                    if (!loggingContext.ErrorWasLogged)
                    {
                        // Logging an error here should be redundant as whatever made the scheduler fail initialization
                        // should have logged an error. But we retain this log message just in case there is an upstream
                        // codepath that isn't logging an error correctly.
                        Logger.Log.SchedulerExportFailedSchedulerNotInitialized(loggingContext);
                    }
                    return false;
                }

                var saveSchedulerTrackerFilesTask = Scheduler.SaveFileChangeTrackerAsync(loggingContext);
                var savingRunningTimeTableTask = TrySaveRunningTimeTable(loggingContext, context, configuration);
                var savingHistoricMetadataCacheTask = TrySaveHistoricMetadataCache(loggingContext, context, configuration);
                var savingFingerprintStoreTask = TrySaveFingerprintStoreAsync(loggingContext, configuration);

                await Task.WhenAll(
                    saveSchedulerTrackerFilesTask,
                    savingRunningTimeTableTask,
                    savingHistoricMetadataCacheTask,
                    savingFingerprintStoreTask);

                return savingRunningTimeTableTask.Result;
            }
        }

        /// <summary>
        /// Attempts to save a running time table for use in future builds.
        /// </summary>
        /// <remarks>
        /// Failure to save produces an error event.
        /// </remarks>
        internal async Task<bool> TrySaveRunningTimeTable(
            LoggingContext loggingContext,
            EngineContext context,
            IConfiguration configuration)
        {
            if (configuration.Schedule.UseHistoricalPerformanceInfo)
            {
                if (!ShouldSerializeOptimizationDataStructurePostExecution())
                {
                    return true;
                }

                // Unblock the caller
                await Task.Yield();

                using (var pm = PerformanceMeasurement.StartWithoutStatistic(
                    loggingContext,
                    Logger.Log.StartSavingHistoricPerfData,
                    Logger.Log.EndSavingHistoricPerfData))
                {
                    HistoricPerfDataTable table = Scheduler.HistoricPerfDataTable;
                    Contract.Assume(table != null);
                    var filePath = GetRunningTimeTableFilePath(context.PathTable, configuration.Layout, pm.LoggingContext);
                    SchedulerLogger.Log.HistoricPerfDataCacheTrace(
                        pm.LoggingContext,
                        I($"Saving historic perf data to path '{filePath ?? string.Empty}'"));

                    if (filePath == null)
                    {
                        Contract.Assume(pm.LoggingContext.WarningWasLogged);

                        // Not using an error message, since we don't have one, the error is described by the file name (which wasn't resolved in this case).
                        Logger.Log.SavingHistoricPerfDataFailed(pm.LoggingContext, "file path is not resolved", string.Empty);
                        return false;
                    }

                    try
                    {
                        SchedulerLogger.Log.HistoricPerfDataCacheTrace(pm.LoggingContext, I($"Saving historic perf data Start"));

                        FileUtilities.DeleteFile(filePath, tempDirectoryCleaner: m_tempCleaner);
                        table.Save(filePath);

                        SchedulerLogger.Log.HistoricPerfDataCacheTrace(pm.LoggingContext, I($"Saving historic perf data Done"));

                        Logger.Log.HistoricPerfDataSaved(pm.LoggingContext, table.Count);
                    }
                    catch (BuildXLException ex)
                    {
                        Logger.Log.SavingHistoricPerfDataFailed(pm.LoggingContext, filePath, ex.LogEventMessage);
                        return false;
                    }

                    Possible<Unit> storeResult;
                    using (Context.EngineCounters.StartStopwatch(EngineCounter.HistoricPerfDataSavingDuration))
                    {
                        var performanceDataFingerprint = PerformanceDataUtilities.ComputePerformanceDataFingerprint(
                            loggingContext,
                            context.PathTable,
                            graphSemistableFingerprint: Scheduler.PipGraph.SemistableFingerprint,
                            environmentFingerprint: configuration.Schedule.EnvironmentFingerprint);
                        storeResult = await m_cache.TryStoreRunningTimeTableAsync(pm.LoggingContext, filePath, Context.PathTable, performanceDataFingerprint);
                    }

                    if (!storeResult.Succeeded)
                    {
                        SchedulerLogger.Log.HistoricPerfDataCacheTrace(
                            pm.LoggingContext,
                            I($"Saving historic perf data to cache failed: {storeResult.Failure.DescribeIncludingInnerFailures()}"));
                    }
                    else
                    {
                        Context.EngineCounters.IncrementCounter(EngineCounter.HistoricPerfDataStoredToCache);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Indicates if the schedule has failed or not.
        /// </summary>
        public bool HasFailed => Scheduler.HasFailed;

        /// <summary>
        /// Indicates if the schedule has encountered a failure or received a cancellation request.
        /// </summary>
        public bool IsTerminating => Scheduler.IsTerminating;

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            if (m_updateStatusAction != null)
            {
                m_updateStatusAction.Cancel();
                m_updateStatusAction.Join();
                m_updateStatusAction.Dispose();
            }

            Context.EngineCounters.MeasuredDispose(SchedulingQueue, EngineCounter.SchedulingQueueDisposeDuration);

            Context.EngineCounters.MeasuredDispose(Scheduler, EngineCounter.SchedulerDisposeDuration);

            // Make sure to dispose the scheduler before the TempCleaner so we make sure no pips are still
            // registering their temp directories to be cleaned.
            Context.EngineCounters.MeasuredDispose(m_tempCleaner, EngineCounter.TempCleanerDisposeDuration);

            Context.EngineCounters.MeasuredDispose(PipTable, EngineCounter.PipTableDisposeDuration);

            Context.EngineCounters.MeasuredDispose(m_cache, EngineCounter.CacheDisposeDuration);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000",
            Justification = "Dispose is indeed being called on the Timer object, not just the Dispose method FxCop expects")]
        internal bool ExecuteScheduledPips(
            LoggingContext loggingContext,
            WorkerService workerService)
        {
            LogDiskFreeSpace(loggingContext, executionStart: true);

            m_schedulerStartTime = TimestampUtilities.Timestamp;

            if (workerService != null)
            {
                var serializer = new ExecutionResultSerializer(MaxSerializedAbsolutePath, Scheduler.Context);
                workerService.Start(this, serializer);
            }

            Scheduler.Start(loggingContext);

            bool success = true;

            m_updateStatusAction = new CancellableTimedAction(
                () => Scheduler.UpdateStatus(overwriteable: true, expectedCallbackFrequency: UpdateStatusIntervalMs),
                UpdateStatusIntervalMs,
                "SchedulerUpdateStatus");

            // For remote workers, we need to make sure that UpdateStatus method is executed when updateStatusAction is started. 
            // Because UpdateStatus sets available memory for the workers, we need to have that info before we send it to the orchestrator. 
            m_updateStatusAction.Start(syncStart: workerService != null);

            if (workerService != null)
            {
                // Remote worker node in a distributed build
                success &= workerService.WhenDoneAsync().GetAwaiter().GetResult();
                Contract.Assert(success || loggingContext.ErrorWasLogged, "WorkerService encountered errors, but none were logged.");
            }

            success &= Scheduler.WhenDone().GetAwaiter().GetResult();
            Contract.Assert(success || loggingContext.ErrorWasLogged, "Scheduler encountered errors, but none were logged.");

            m_updateStatusAction.Cancel();
            m_updateStatusAction.Join();

            LogDiskFreeSpace(loggingContext, executionStart: false);

            // Report one final status
            Scheduler.UpdateStatus(overwriteable: false);

            PipTable.StopBackgroundSerialization();
            PipTable.WhenDone().Wait();

            return success;
        }

        private static void LogDiskFreeSpace(LoggingContext loggingContext, bool executionStart)
        {
            string category = executionStart ? Statistics.DiskAvailableMBExecutionStart : Statistics.DiskAvailableMBExecutionEnd;
            List<KeyValuePair<string, int>> drives = new List<KeyValuePair<string, int>>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed && drive.Name.Length > 0)
                {
                    int availableMegabytes = (int)(drive.AvailableFreeSpace / (1024 * 1024));
                    drives.Add(new KeyValuePair<string, int>(drive.Name.Substring(0, 1), availableMegabytes));
                }
            }

            if (drives.Count > 0)
            {
                LoggingHelpers.LogCategorizedStatistics(loggingContext, category, drives);
            }
        }

        /// <summary>
        /// At the end of the build this logs some important stats about the build
        /// </summary>
        public SchedulerPerformanceInfo LogStats(LoggingContext loggingContext, [AllowNull] BuildSummary buildSummary)
        {
#pragma warning disable SA1114 // Parameter list must follow declaration

            if (BuildXL.Engine.ETWLogger.Log.IsEnabled(Diagnostics.EventLevel.Verbose, Keywords.Diagnostics))
            {
                Logger.Log.PipTableStats(
                    loggingContext,
                    PipTable.PageStreamsCount,
                    PipTable.Size,
                    PipTable.Used,
                    PipTable.Count,
                    PipTable.Writes,
                    PipTable.Reads,
                    PipTable.Alive,
                    PipTable.WritesMilliseconds,
                    PipTable.ReadsMilliseconds);

                foreach (var kvp in PipTable.DeserializationContexts.OrderByDescending(kvp => kvp.Value))
                {
                    Logger.Log.PipTableDeserializationContext(loggingContext, kvp.Key.ToString(), kvp.Value);
                }
#pragma warning restore SA1114 // Parameter list must follow declaration
            }

            var schedulerPerformance = Scheduler.LogStats(loggingContext, buildSummary);

            // Log allowlist file statistics
            if (m_configFileState.FileAccessAllowlist != null && m_configFileState.FileAccessAllowlist.MatchedEntryCounts.Count > 0)
            {
                Logger.Log.AllowlistFileAccess(loggingContext, m_configFileState.FileAccessAllowlist.MatchedEntryCounts);
            }

            return schedulerPerformance;
        }

#region Serialization

        /// <summary>
        /// Attempts to load an EngineSchedule from disk.
        /// </summary>
        /// <returns>
        /// If successful, returns an EngineSchedueReuse containing the EngineSchedule and any other associated
        /// objects that must be used in conjunction with the returned EngineSchedule. Returns null if unsuccessful
        /// </returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The disposable objects ownership is handed over to the returned EngineSchedule that is responsible for disposing.")]
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static async Task<Tuple<EngineSchedule, EngineContext, IConfiguration>> LoadAsync(
            EngineContext oldContext,
            EngineSerializer serializer,
            CacheInitializationTask engineCacheInitializationTask,
            FileContentTable fileContentTable,
            JournalState journalState,
            IConfiguration configuration,
            LoggingContext loggingContext,
            PerformanceCollector performanceCollector,
            DirectoryTranslator directoryTranslator,
            EngineState engineState,
            TempCleaner tempCleaner,
            string buildEngineFingerprint,
            PipSpecificPropertiesConfig pipSpecificPropertiesConfig,
            IConsole console)
        {
            // journal may be null, in the event that journal usage is not enabled.
            var graphLoaderAndFingerprint = await ReadGraphFingerprintAndCreateGraphLoader(oldContext, serializer, loggingContext, engineState);

            if (graphLoaderAndFingerprint == null)
            {
                return null;
            }

            CachedGraphLoader loadingGraph = graphLoaderAndFingerprint.Item1;
            ContentFingerprint semistableFingerprintOfGraphToReload = graphLoaderAndFingerprint.Item2;

            // Create a task for each deserialization item so they can pass dependencies on other structures as tasks and not block on one another.
            var pathTableTask = loadingGraph.GetOrLoadPathTableAsync();
            var symbolTableTask = loadingGraph.GetOrLoadSymbolTableAsync();
            var mountPathExpanderTask = loadingGraph.GetOrLoadMountPathExpanderAsync();
            var pipExecutionContextTask = loadingGraph.GetOrLoadPipExecutionContextAsync();
            var historicTableSizesTask = loadingGraph.GetOrLoadHistoricDataAsync();
            var pipTableTask = loadingGraph.GetOrLoadPipTableAsync();
            var pipGraphTask = loadingGraph.GetOrLoadPipGraphAsync();

            // DeserializeFromFile() performs all exception handling so accessing Result is safe and will either return a valid object or null
            var configFileStateTask = serializer.DeserializeFromFileAsync<ConfigFileState>(
                GraphCacheFile.ConfigState,
                reader => ConfigFileState.DeserializeAsync(reader, pipExecutionContextTask));

            EngineContext newContext;

            if (await pipExecutionContextTask != null)
            {
                var pathTable = (await pipExecutionContextTask).PathTable;
                newContext = new EngineContext(
                    oldContext.CancellationToken,
                    pathTable,
                    (await pipExecutionContextTask).SymbolTable,
                    (await pipExecutionContextTask).QualifierTable,
                    oldContext.FileSystem.CopyWithNewPathTable(pathTable),
                    new TokenTextTable(),
                    engineCounters: oldContext.EngineCounters,
                    historicTableSizes: await historicTableSizesTask,
                    console: console);
            }
            else
            {
                // This is a required component.
                return null;
            }

            var configFileState = await configFileStateTask;
            if (configFileState == null)
            {
                // This is a required component.
                return null;
            }

            IConfiguration newConfiguration;
            using (PerformanceMeasurement.StartWithoutStatistic(
                loggingContext,
                Logger.Log.StartRehydratingConfigurationWithNewPathTable,
                Logger.Log.EndRehydratingConfigurationWithNewPathTable))
            {
                var mutableConfiguration = new BuildXL.Utilities.Configuration.Mutable.ConfigurationImpl(
                    configuration,
                    new BuildXL.Utilities.Configuration.Mutable.PathRemapper(oldContext.PathTable, newContext.PathTable));
                newConfiguration = configFileState.MergeIntoConfiguration(mutableConfiguration);
            }

            var performanceDataFingerprint = PerformanceDataUtilities.ComputePerformanceDataFingerprint(
                loggingContext,
                newContext.PathTable,
                graphSemistableFingerprint: semistableFingerprintOfGraphToReload,
                environmentFingerprint: configuration.Schedule.EnvironmentFingerprint);

            AsyncLazy<HistoricPerfDataTable> runningTimeTable = Lazy.CreateAsync(
                () =>
                    TryLoadRunningTimeTable(
                        loggingContext,
                        newContext,
                        newConfiguration,
                        GetCacheForContext(engineCacheInitializationTask),
                        performanceDataFingerprint: performanceDataFingerprint));

            // We try to wait on the cache near to last (we happen to track the first wait attempt on the cache relative to when it is actually ready).
            Possible<CacheInitializer> possibleCacheInitializer = await engineCacheInitializationTask;

            // We always need an EngineCache to put together a scheduler; if that fails, we have to give up (unfortunately after waiting on the tasks prior).
            if (!possibleCacheInitializer.Succeeded)
            {
                return null;
            }

            CacheInitializer cacheInitializer = possibleCacheInitializer.Result;

            // newContext is the finalized EngineContext. Now we can construct anything that needs a context.
            // Note that the proper EngineCache is one such thing, and so now we are responsible for disposing it later
            // (rather than EngineCache, which is initialized before we have a context ready).
            EngineCache scheduleCache = cacheInitializer.CreateCacheForContext();

            var pathExpander = await mountPathExpanderTask;
            PipTwoPhaseCache pipTwoPhaseCache = InitTwoPhaseCache(
                    loggingContext,
                    newContext,
                    newConfiguration,
                    scheduleCache,
                    performanceDataFingerprint: performanceDataFingerprint,
                    pathExpander: pathExpander);

            await serializer.WaitForPendingDeserializationsAsync();

            if (await configFileStateTask != null &&
                await pipTableTask != null &&
                await pipExecutionContextTask != null &&
                await pipGraphTask != null)
            {
                var pipQueue = new PipQueue(loggingContext, newConfiguration);

                var pathTable = await pathTableTask;

                PreserveOutputsInfo? previousOutputsSalt = PreparePreviousOutputsSalt(loggingContext, pathTable, newConfiguration);
                if (!previousOutputsSalt.HasValue)
                {
                    Contract.Assume(loggingContext.ErrorWasLogged);
                    return null;
                }

                Scheduler.Scheduler scheduler;

                try
                {
                    scheduler = new Scheduler.Scheduler(
                        await pipGraphTask,
                        pipQueue,
                        await pipExecutionContextTask,
                        fileContentTable,
                        cache: scheduleCache,
                        configuration: newConfiguration,
                        journalState: journalState,
                        loggingContext: loggingContext,
                        fileAccessAllowlist: configFileState.FileAccessAllowlist,
                        globalReclassificationRules: configFileState.GlobalReclassificationRules,
                        directoryMembershipFingerprinterRules: configFileState.DirectoryMembershipFingerprinterRules,
                        runningTimeTable: runningTimeTable,
                        tempCleaner: tempCleaner,
                        performanceCollector: performanceCollector,
                        previousInputsSalt: previousOutputsSalt.Value,
                        fingerprintSalt: newConfiguration.Cache.CacheSalt,
                        directoryTranslator: directoryTranslator,
                        pipTwoPhaseCache: pipTwoPhaseCache,
                        buildEngineFingerprint: buildEngineFingerprint,
                        vmInitializer: VmInitializer.CreateFromEngine(
                            newConfiguration.Layout.BuildEngineDirectory.ToString(newContext.PathTable),
                            newConfiguration.Layout.ExternalSandboxedProcessDirectory.ToString(newContext.PathTable),
                            vmCommandProxyAlternate: EngineEnvironmentSettings.VmCommandProxyPath,
                            newConfiguration.Logging.SubstSource.IsValid && newConfiguration.Logging.SubstTarget.IsValid
                                ? FileUtilities.GetSubstDriveAndPath(
                                    newConfiguration.Logging.SubstSource.ToString(newContext.PathTable),
                                    newConfiguration.Logging.SubstTarget.ToString(newContext.PathTable))
                                : default,
                            message => Logger.Log.StartInitializingVm(loggingContext, message),
                            message => Logger.Log.EndInitializingVm(loggingContext, message),
                            message => Logger.Log.InitializingVm(loggingContext, message)),
                        pipSpecificPropertiesConfig: pipSpecificPropertiesConfig);
                }
                catch (BuildXLException e)
                {
                    Contract.Assert(
                        loggingContext.ErrorWasLogged,
                        $"Unable to construct schedule during loading, but no error was logged.  Exception caught: {e}");
                    scheduleCache.Dispose();
                    return null;
                }

                // We pass ownership of the created scheduleCache to the resultant EngineSchedule.
                // It should dispose the scheduleCache eventually.
                var engineSchedule = Create(
                    loggingContext,
                    newContext,
                    fileContentTable,
                    await pipTableTask,
                    scheduler,
                    scheduleCache,
                    pathExpander,
                    pipQueue,
                    tempCleaner,
                    await configFileStateTask,
                    configuration.FrontEnd.MaxFrontEndConcurrency());

                if (engineSchedule == null)
                {
                    scheduler.Dispose();
                    scheduleCache.Dispose();
                    return null;
                }

                return Tuple.Create(engineSchedule, newContext, newConfiguration);
            }

            return null;
        }

        /// <summary>
        /// Attempts to load the PipGraph from disk.
        /// </summary>
        /// <returns>
        /// If successful, returns a tuple containing the graph and the engine context; otherwise, returns null.
        /// </returns>
        public static async Task<Tuple<PipGraph, EngineContext>> LoadPipGraphAsync(
            EngineContext oldContext,
            EngineSerializer serializer,
            LoggingContext loggingContext,
            EngineState engineState,
            IConsole console)
        {
            var graphLoaderAndFingerprint = await ReadGraphFingerprintAndCreateGraphLoader(oldContext, serializer, loggingContext, engineState);

            if (graphLoaderAndFingerprint == null)
            {
                return null;
            }

            CachedGraphLoader loadingGraph = graphLoaderAndFingerprint.Item1;

            PipExecutionContext pipExecutionContext = await loadingGraph.GetOrLoadPipExecutionContextAsync();
            if (pipExecutionContext == null)
            {
                return null;
            }

            PipGraph pipGraph = await loadingGraph.GetOrLoadPipGraphAsync();
            if (pipGraph == null)
            {
                return null;
            }

            var historicTableSizes = await loadingGraph.GetOrLoadHistoricDataAsync();
            var newContext = new EngineContext(
                oldContext.CancellationToken,
                pipExecutionContext.PathTable,
                pipExecutionContext.SymbolTable,
                new QualifierTable(pipExecutionContext.StringTable),
                oldContext.FileSystem.CopyWithNewPathTable(pipExecutionContext.PathTable),
                new TokenTextTable(),
                engineCounters: oldContext.EngineCounters,
                historicTableSizes: historicTableSizes,
                console: console);

            return Tuple.Create(pipGraph, newContext);
        }

        private static async Task<Tuple<CachedGraphLoader, ContentFingerprint>> ReadGraphFingerprintAndCreateGraphLoader(
            EngineContext oldContext,
            EngineSerializer serializer,
            LoggingContext loggingContext,
            EngineState engineState)
        {
            // Just read the graphId guid, which is the first entry of PipGraph file after header.
            // It will not load the whole file to the memory as the eagerIO parameter is false.
            // GraphId guid will help us decide whether EngineState can be reused or not.
            var graphIdAndSemistableFingerprintOfGraphToReload = await serializer.DeserializeFromFileAsync(GraphCacheFile.PipGraphId, PipGraph.DeserializeGraphIdAsync);

            if (graphIdAndSemistableFingerprintOfGraphToReload == null)
            {
                return null;
            }

            var graphIdOfGraphToReload = graphIdAndSemistableFingerprintOfGraphToReload.Item1;

            CachedGraphLoader loadingGraph = engineState?.TryLoad(loggingContext, oldContext.CancellationToken, graphIdOfGraphToReload);
            if (loadingGraph == null)
            {
                loadingGraph = CachedGraphLoader.CreateFromDisk(oldContext.CancellationToken, serializer);
            }

            return Tuple.Create(loadingGraph, graphIdAndSemistableFingerprintOfGraphToReload.Item2);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        internal EngineState GetOrCreateNewEngineState(EngineState previousEngineState)
        {
            EngineState engineState;

            // If the previous engine state is null or disposed, then create a new one.
            if (!EngineState.IsUsable(previousEngineState))
            {
                engineState = EngineState.CreateNew(this);
            }
            else
            {
                // Create the new one from the previous state
                // We update the scheduler state every time because the developer might pass a different filter
                // and the file content table with the new instance that was created this run.
                engineState = previousEngineState
                    .WithUpdatedSchedulerState(Scheduler)
                    .WithUpdatedFileContentTable(FileContentTable);
            }

            bool isPipTableTransferred = TransferPipTableOwnership(engineState.PipTable);
            Contract.Assert(isPipTableTransferred);

            return engineState;
        }

        /// <summary>
        /// Transfer the ownership of PipTable to TestHooks, EngineState or EngineLiveVisualizationInformation
        /// </summary>
        /// <returns>True if the ownership was transfered either in this call or if the <see cref="EngineSchedule"/>
        /// no longer owns a <see cref="PipTable"/>. It may no longer own the object due to a prior successful
        /// transfer or due to never having owned a reference.</returns>
        public bool TransferPipTableOwnership(PipTable table)
        {
            if (PipTable != null && PipTable != table)
            {
                // It has a different PipTable so do not transfer the ownership.
                return false;
            }

            // PipTable has (already) been transferred
            PipTable = null;
            return true;
        }

        /// <summary>
        /// Synchronously saves the schedule to disk for reuse in a future run
        /// </summary>
        /// <returns>whether the operation succeeded</returns>
        internal async Task<bool> SaveToDiskAsync(EngineSerializer serializer, EngineContext context)
        {
            var executionStateTasks = SaveExecutionStateToDiskAsync(
                serializer,
                context,
                PipTable,
                Scheduler.PipGraph,
                MountPathExpander,
                context.NextHistoricTableSizes);

            // EngineSchedule specific state
            var result = await serializer.SerializeToFileAsync(GraphCacheFile.ConfigState, m_configFileState.Serialize);

            return (await executionStateTasks) && result.Success;
        }

        /// <summary>
        /// Synchronously saves the subset of scheduling state needed for execution analyzers.
        /// </summary>
        internal static async Task<bool> SaveExecutionStateToDiskAsync(
            EngineSerializer serializer,
            BuildXLContext context,
            PipTable pipTable,
            PipGraph pipGraph,
            MountPathExpander mountPathExpander,
            HistoricTableSizes historicTableSizes)
        {
            var tasks = new[]
                {
                    serializer.SerializeToFileAsync(GraphCacheFile.PathTable, context.PathTable.Serialize),
                    serializer.SerializeToFileAsync(GraphCacheFile.StringTable, context.StringTable.Serialize),
                    serializer.SerializeToFileAsync(GraphCacheFile.SymbolTable, context.SymbolTable.Serialize),
                    serializer.SerializeToFileAsync(GraphCacheFile.QualifierTable, context.QualifierTable.Serialize),
                    serializer.SerializeToFileAsync(GraphCacheFile.PipTable, writer => pipTable.Serialize(writer, PipTableMaxDegreeOfParallelismDuringSerialization)),
                    serializer.SerializeToFileAsync(GraphCacheFile.PipGraph, pipGraph.Serialize),
                    serializer.SerializeToFileAsync(GraphCacheFile.PipGraphId, pipGraph.SerializeGraphId),
                    serializer.SerializeToFileAsync(GraphCacheFile.DirectedGraph, pipGraph.DirectedGraph.Serialize),
                    serializer.SerializeToFileAsync(GraphCacheFile.MountPathExpander, mountPathExpander.Serialize),
                    serializer.SerializeToFileAsync(GraphCacheFile.HistoricTableSizes, historicTableSizes.Serialize),
                };

            var results = await Task.WhenAll(tasks);

            return results.All(a => a.Success);
        }

        /// <summary>
        /// Saves the schedule to the cache
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        internal async Task<bool> TrySaveToCacheAsync(
            EngineSerializer serializer,
            InputTracker tracker,
            LoggingContext loggingContext,
            RootTranslator rootTranslator,
            IReadOnlyDictionary<string, string> envVarsImpactingBuild,
            IReadOnlyDictionary<string, IMount> mountsImpactingBuild,
            IReadOnlyDictionary<string, string> availableEnvVars,
            IReadOnlyDictionary<string, IMount> availableMounts,
            AsyncOut<PipGraphCacheDescriptor> descriptorOut = null,
            AsyncOut<ContentFingerprint> identifierFingerprintOut = null)
        {
            // Add the files that make up the serialized schedule to the cache
            var filesToPut = new List<AbsolutePath>();
            var fileTypes = new List<GraphCacheFile>();
            foreach (var t in serializer.SerializationTasks)
            {
                var serializationTaskResult = await t;
                if (serializationTaskResult.Success)
                {
                    fileTypes.Add(serializationTaskResult.FileType);
                    filesToPut.Add(
                        AbsolutePath.Create(Context.PathTable, serializationTaskResult.FullPath));
                }
                else
                {
                    return false;
                }
            }

            var storeTasks = new List<Task<Possible<ContentHash, Failure>>>(filesToPut.Count);
            foreach (AbsolutePath fileToPut in filesToPut)
            {
                string translatedPath = rootTranslator.Translate(fileToPut.ToString(Context.PathTable));
                storeTasks.Add(
                    Task.Run(
                        () => m_cache.ArtifactContentCache.TryStoreAsync(
                            FileRealizationMode.HardLinkOrCopy,
                            new ExpandedAbsolutePath(AbsolutePath.Create(Context.PathTable, translatedPath), Context.PathTable))));
            }

            Possible<ContentHash, Failure>[] storeResults = await Task.WhenAll(storeTasks);

            // Compute a fingerprint for this graph based on the content of the graph files (this is a very strong identity).
            using (var hasher = new CoreHashingHelper(false))
            {
                List<StringKeyedHash> hashes = new List<StringKeyedHash>(storeResults.Length);

                hasher.Add("CachedEngineScheduleFiles", storeResults.Length);

                for (int i = 0; i < storeResults.Length; i++)
                {
                    Possible<ContentHash> result = storeResults[i];
                    if (!result.Succeeded)
                    {
                        Logger.Log.FailedToSaveGraphToCache(loggingContext, false, result.Failure.DescribeIncludingInnerFailures());
                        return false;
                    }

                    hashes.Add(new StringKeyedHash { Key = filesToPut[i].ToString(Context.PathTable), ContentHash = result.Result.ToByteString() });
                }

                // We can sort in place since nobody uses 'hashes' after this point in an order dependent way.
                hashes.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
                foreach (var pathAndHashPair in hashes)
                {
                    hasher.Add(pathAndHashPair.Key, pathAndHashPair.ContentHash.ToContentHash());
                }

                var identifierFingerprint = new ContentFingerprint(hasher.GenerateHash());

                if (identifierFingerprintOut != null)
                {
                    identifierFingerprintOut.Value = identifierFingerprint;
                }
            }

            // Create a PipGraphCacheDescriptor from the (file type, hash) pairs of all files that make up the serialized graph.
            // We learned the hashes from the cache put.
            // The list of consumed files and their hashes are saved in one of the serialized files instead of as inputs in
            // this cache descriptor. This is done so the graph caching can be used without the cache being enabled.
            // This descriptor will be stored under both fingerprints (input based and graph content based) to allow finding the graph content
            // from either the input files themselves (has a graph for these specs been computed before?) or from logs (load the exact graph used in a prior build).
            var hashesByFileType = new Dictionary<GraphCacheFile, ByteString>();
            Contract.Assume(fileTypes.Count == storeResults.Length);
            for (int i = 0; i < fileTypes.Count; i++)
            {
                Contract.Assume(storeResults[i].Succeeded);
                hashesByFileType.Add(fileTypes[i], storeResults[i].Result.ToByteString());
            }

            PipGraphCacheDescriptor descriptor = PipGraphCacheDescriptor.CreateFromFiles(
                hashesByFileType,
                traceInfo: loggingContext.Session.Environment);

            if (descriptorOut != null)
            {
                descriptorOut.Value = descriptor;
            }

            var cachedGraphProvider = new CachedGraphProvider(loggingContext, Context, m_cache, FileContentTable, m_maxDegreeOfParallelism);

            var storedDescriptor = await cachedGraphProvider.TryStorePipGraphCacheDescriptorAsync(
                tracker,
                BuildParameters.GetFactory().PopulateFromDictionary(envVarsImpactingBuild),
                mountsImpactingBuild,
                BuildParameters.GetFactory().PopulateFromDictionary(availableEnvVars),
                availableMounts,
                descriptor);

            return true;
        }

        /// <summary>
        /// Attempts to fetch the schedule from the cache
        /// </summary>
        public static async Task<bool> TryFetchFromCacheAsync(
            LoggingContext loggingContext,
            PipExecutionContext context,
            EngineCache cache,
            PipGraphCacheDescriptor cacheDescriptor,
            EngineSerializer serializer,
            FileContentTable fileContentTable,
            ITempCleaner tempDirectoryCleaner)
        {
            if (cacheDescriptor != null)
            {
                // Now fetch all of the files making up the serialized schedule
                var hashesToFetch = new List<ContentHash>();
                var pathsToPopulate = new List<AbsolutePath>();

                foreach (var graphFileAndHash in cacheDescriptor.EnumerateGraphFiles())
                {
                    string finalPath = serializer.GetFullPath(graphFileAndHash.Key);
                    string placementPath = finalPath;

                    // The PreviousInputs file gets moved from an intermediate to a final location to specify whether
                    // the graph files are valid to be consumed. So the check to see whether it is up to date needs to
                    // happen on its finalized location, not the intermediate.
                    if (finalPath.Equals(serializer.PreviousInputsFinalized, OperatingSystemHelper.PathComparison))
                    {
                        placementPath = serializer.PreviousInputsIntermediate;
                    }

                    // Skip retrieving the file from cache if the existing version is already up to date
                    if (File.Exists(finalPath))
                    {
                        // Only check if the hash of the file is already known. If the file is not in the FileContentTable
                        // we might as well copy (or hardlink) over it
                        VersionedFileIdentityAndContentInfo? hash = fileContentTable.TryGetKnownContentHash(finalPath);

                        if (hash.HasValue && graphFileAndHash.Value.ToContentHash() == hash.Value.FileContentInfo.Hash)
                        {
                            continue;
                        }

                        FileUtilities.DeleteFile(finalPath, tempDirectoryCleaner: tempDirectoryCleaner);
                        FileUtilities.DeleteFile(placementPath, tempDirectoryCleaner: tempDirectoryCleaner);
                    }

                    hashesToFetch.Add(graphFileAndHash.Value.ToContentHash());
                    pathsToPopulate.Add(AbsolutePath.Create(context.PathTable, placementPath));
                }

                // We must establish availability before trying to materialize content.
                Possible<ContentAvailabilityBatchResult> maybeLoaded =
                    await cache.ArtifactContentCache.TryLoadAvailableContentAsync(hashesToFetch, context.CancellationToken);

                if (!maybeLoaded.Succeeded)
                {
                    Logger.Log.FailedToFetchSerializedGraphFromCache(loggingContext, maybeLoaded.Failure.DescribeIncludingInnerFailures());
                    return false;
                }

                ContentAvailabilityBatchResult availability = maybeLoaded.Result;
                if (!availability.AllContentAvailable)
                {
                    // Innocent cache miss
                    return false;
                }

                Contract.Assert(pathsToPopulate.Count == hashesToFetch.Count);

                var materializationTasks = pathsToPopulate.Select((path, i) => Task.Run(() => cache.ArtifactContentCache.TryMaterializeAsync(
                        FileRealizationMode.HardLinkOrCopy,
                        path.Expand(context.PathTable),
                        hashesToFetch[i],
                        context.CancellationToken))).ToArray();

                var results = await Task.WhenAll(materializationTasks);

                for (int i = 0; i < pathsToPopulate.Count; i++)
                {
                    Possible<Unit> maybePlaced = results[i];

                    if (!maybePlaced.Succeeded)
                    {
                        Logger.Log.FailedToFetchSerializedGraphFromCache(loggingContext, maybePlaced.Failure.DescribeIncludingInnerFailures());
                        return false;
                    }
                }

                // If the PreviousInputs file was fetched it will be at its intermediate location. It must be finalized
                // before the cached schedule may be reused
                if (File.Exists(serializer.PreviousInputsIntermediate))
                {
                    if (!serializer.FinalizePreviousInputsFile())
                    {
                        return false;
                    }
                }

                // Delete previous inputs journal checkpoint to ensure that input tracker and file change tracker in-sync.
                // This is just an additional safety mechanism. Input tracker itself has already had a logic to handle
                // the case when the trackers are out of sync.
                serializer.TryDeletePreviousInputsJournalCheckpointFile();

                return true;
            }

            return false;
        }

        internal static Task DuplicateScheduleFiles(LoggingContext loggingContext, EngineSerializer serializer, string destinationFolder)
        {
            return DuplicateFiles(
                loggingContext,
                serializer,
                destinationFolder,
                new[]
                {
                    EngineSerializer.StringTableFile,
                    EngineSerializer.PathTableFile,
                    EngineSerializer.SymbolTableFile,
                    EngineSerializer.QualifierTableFile,
                    EngineSerializer.PipTableFile,
                    EngineSerializer.MountPathExpanderFile,
                    EngineSerializer.DirectedGraphFile,
                    EngineSerializer.PipGraphFile,
                    EngineSerializer.ConfigFileStateFile,
                    EngineSerializer.HistoricTableSizes
                });
        }

        internal static Task DuplicatePreviousInputFiles(LoggingContext loggingContext, EngineSerializer serializer, string destinationFolder)
        {
            return DuplicateFiles(
                loggingContext,
                serializer,
                destinationFolder,
                new[] { EngineSerializer.PreviousInputsFile },
                // The previous input journal file may not exist, e.g., BuildXL had a graph cache hit from the content cache.
                // Note that only previous inputs file is stored in the content cache. But BuildXL always correlate the previous inputs
                // with its journal checkpoint file. So, if they are not synced, then the journal checkpoint file is re-created.
                optionalFileNames: new[] { EngineSerializer.PreviousInputsJournalCheckpointFile });
        }

        private static async Task DuplicateFiles(
            LoggingContext loggingContext,
            EngineSerializer serializer,
            string destinationFolder,
            string[] fileNames,
            string[] optionalFileNames = null)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            var duplicateTasks = fileNames.Select(fileName => TryDuplicateOneFile(loggingContext, serializer, fileName, destinationFolder)).ToList();
            if (optionalFileNames != null && optionalFileNames.Length > 0)
            {
                duplicateTasks.AddRange(optionalFileNames.Select(fileName => TryDuplicateOneFile(loggingContext, serializer, fileName, destinationFolder, optional: true)));
            }

            await Task.WhenAll(duplicateTasks);

            Logger.Log.FinishedCopyingGraphToLogDir(loggingContext, string.Join(", ", fileNames), sw.ElapsedMilliseconds);
        }

        private static async Task<bool> TryDuplicateOneFile(
            LoggingContext loggingContext,
            EngineSerializer serializer,
            string fileName,
            string destinationFolder,
            bool optional = false)
        {
            string sourcePath = serializer.GetFullPath(fileName);
            string destinationPath = Path.Combine(destinationFolder, fileName);

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await FileUtilitiesExtensions.TryDuplicateOneFileAsync(sourcePath, destinationPath);

                if (result == FileDuplicationResult.Copied)
                {
                    Logger.Log.FallingBackOnGraphFileCopy(loggingContext, sourcePath, destinationFolder, sw.ElapsedMilliseconds);
                }
            }
            catch (BuildXLException ex)
            {
                if (!optional)
                {
                    Logger.Log.FailedToDuplicateGraphFile(loggingContext, fileName, destinationFolder, (ex.InnerException ?? ex).Message);
                    return false;
                }

                Logger.Log.FailedToDuplicateOptionalGraphFile(loggingContext, fileName, destinationFolder, (ex.InnerException ?? ex).Message);
                return true;
            }

            return true;
        }

        #endregion
    }
}
