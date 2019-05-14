// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine.Cache.Plugin.CacheCore;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;
using ICacheCoreCache = BuildXL.Cache.Interfaces.ICache;
using ICacheCoreSession = BuildXL.Cache.Interfaces.ICacheSession;

namespace BuildXL.Engine
{
    /// <summary>
    /// Cache abstraction for the engine, abstracting initialization. Cache initialization is asynchronous (it may be long running in practice).
    /// </summary>
    public abstract class CacheInitializer : IDisposable
    {
        /// <nodoc />
        protected readonly LoggingContext LoggingContext;

        private readonly List<IDisposable> m_acquiredDisposables;

        // Must be kept in sync with the error message defined in LockAcquisitionResult.cs
        private const string LockAcquisitionFailureMessagePrefix = "Failed to acquire single instance lock for";

        /// <nodoc />
        protected CacheInitializer(
            LoggingContext loggingContext,
            List<IDisposable> acquiredDisposables,
            bool enableFingerprintLookup)
        {
            LoggingContext = loggingContext;
            m_acquiredDisposables = acquiredDisposables;
            IsFingerprintLookupEnabled = enableFingerprintLookup;
        }

        /// <summary>
        /// Indicates if the cache is using a real fingerprint store.
        /// </summary>
        public bool IsFingerprintLookupEnabled { get; }

        /// <summary>
        /// Creates a cache for the specified context. Note that a new cache should be obtained when the
        /// context changes, such as when a prior context was reloaded from disk (which may itself have come from a cache).
        /// Any prior instance of <see cref="EngineCache"/> should not be used with data (such as paths)
        /// from the newly installed context.
        /// The returned instance is owned by this <see cref="CacheInitializer"/> wrapper and should not outlive it.
        /// </summary>
        public abstract EngineCache CreateCacheForContext();

        /// <summary>
        /// Creates a task that creates a derived <see cref="CacheInitializer"/> instance that can be used to initialize a cache
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public static CacheInitializationTask GetCacheInitializationTask(
            LoggingContext loggingContext,
            PathTable pathTable,
            string cacheDirectory,
            ICacheConfiguration config,
            RootTranslator rootTranslator,
            bool? recoveryStatus,
            CancellationToken cancellationToken,

            // Only used for testing purposes to inject cache.
            Func<EngineCache> testHookCacheFactory = null)
        {
            Contract.Requires(recoveryStatus.HasValue, "Recovery attempt should have been done before initializing the cache");
            DateTime startTime = DateTime.UtcNow;

            var task = Task.Run(
                async () =>
                {
                    using (PerformanceMeasurement.Start(
                        loggingContext,
                        "CacheInitialization",
                        Tracing.Logger.Log.StartInitializingCache,
                        Tracing.Logger.Log.EndInitializingCache))
                    {
                        if (testHookCacheFactory != null)
                        {
                            return new MemoryCacheInitializer(
                                testHookCacheFactory,
                                loggingContext,
                                new List<IDisposable>(),
                                enableFingerprintLookup: config.Incremental);
                        }

                        Possible<CacheCoreCacheInitializer> maybeCacheCoreEngineCache =
                            await CacheCoreCacheInitializer.TryInitializeCacheInternalAsync(
                                loggingContext,
                                pathTable,
                                cacheDirectory,
                                config,
                                enableFingerprintLookup: config.Incremental,
                                rootTranslator: rootTranslator);

                        if (!maybeCacheCoreEngineCache.Succeeded)
                        {
                            string errorMessage = maybeCacheCoreEngineCache.Failure.Describe();
                            if (errorMessage.Contains(LockAcquisitionFailureMessagePrefix))
                            {
                                Tracing.Logger.Log.FailedToAcquireDirectoryLock(
                                    loggingContext,
                                    maybeCacheCoreEngineCache.Failure.DescribeIncludingInnerFailures());
                            }
                            else
                            {
                                Tracing.Logger.Log.StorageCacheStartupError(
                                    loggingContext,
                                    maybeCacheCoreEngineCache.Failure.DescribeIncludingInnerFailures());
                            }
                        }

                        return maybeCacheCoreEngineCache.Then<CacheInitializer>(c => c);
                    }
                }, cancellationToken);

            return new CacheInitializationTask(
                loggingContext,
                startTime,
                task,
                cancellationToken);
        }

        /// <summary>
        /// Closes the cache session if this initializer opened one
        /// </summary>
        public abstract Possible<string, Failure> Close();

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public virtual void Dispose()
        {
            // Dispose in reverse acquisition order.
            for (int i = m_acquiredDisposables.Count - 1; i >= 0; i--)
            {
                m_acquiredDisposables[i].Dispose();
                m_acquiredDisposables.RemoveAt(i);
            }
        }

        /// <summary>
        /// Logs statistics about the cache
        /// </summary>
        internal abstract void LogStats(LoggingContext context);
    }

    internal sealed class MemoryCacheInitializer : CacheInitializer
    {
        private readonly Func<EngineCache> m_cacheFactory;

        public MemoryCacheInitializer(
            Func<EngineCache> cacheFactory,
            LoggingContext loggingContext,
            List<IDisposable> acquiredDisposables,
            bool enableFingerprintLookup)
                : base(
                    loggingContext,
                    acquiredDisposables,
                    enableFingerprintLookup)
        {
            Contract.Requires(cacheFactory != null);

            m_cacheFactory = cacheFactory;
        }

        public override EngineCache CreateCacheForContext()
        {
            var cache = m_cacheFactory();

            return new EngineCache(
                contentCache: cache.ArtifactContentCache,
                twoPhaseFingerprintStore: IsFingerprintLookupEnabled ?
                cache.TwoPhaseFingerprintStore
                : new EmptyTwoPhaseFingerprintStore());
        }

        public override Possible<string, Failure> Close()
        {
            return new Possible<string, Failure>("Success, no session to close.");
        }

        internal override void LogStats(LoggingContext context)
        {
        }
    }

    internal sealed class CacheCoreCacheInitializer : CacheInitializer
    {
        private readonly ICacheCoreCache m_cache;
        private readonly ICacheCoreSession m_session;
        private readonly RootTranslator m_rootTranslator;
        private readonly IDictionary<string, long> m_initialStatistics;

        private CacheCoreCacheInitializer(
            LoggingContext loggingContext,
            ICacheCoreCache cache,
            ICacheCoreSession session,
            List<IDisposable> acquiredDisposables,
            bool enableFingerprintLookup,
            RootTranslator rootTranslator)
            : base(
                loggingContext,
                acquiredDisposables,
                enableFingerprintLookup)
        {
            Contract.Requires(cache != null);
            Contract.Requires(session != null);
            m_cache = cache;
            m_session = session;
            m_rootTranslator = rootTranslator;
            m_initialStatistics = GetCacheBulkStatistics(session);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public override EngineCache CreateCacheForContext()
        {
            IArtifactContentCache contentCache = new CacheCoreArtifactContentCache(
                m_session,
                rootTranslator: m_rootTranslator);

            ITwoPhaseFingerprintStore twoPhase;
            if (IsFingerprintLookupEnabled)
            {
                twoPhase = new CacheCoreFingerprintStore(m_session);
            }
            else
            {
                twoPhase = new EmptyTwoPhaseFingerprintStore();
            }

            return new EngineCache(contentCache, twoPhase);
        }

        /// <summary>
        /// Gets an instance of <see cref="ICacheConfigData"/> from cache configuration.
        /// </summary>
        internal static Possible<ICacheConfigData> TryGetCacheConfigData(
            PathTable pathTable,
            string cacheDirectory,
            ICacheConfiguration config)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(pathTable.IsValid);
            Contract.Requires(config != null);
            Contract.Requires(config.CacheLogFilePath.IsValid);
            Contract.Requires(config.CacheConfigFile.IsValid);
            Contract.Requires(!string.IsNullOrWhiteSpace(cacheDirectory));

            Possible<string> maybeConfigData = TryReadCacheConfigFile(config.CacheConfigFile.ToString(pathTable));

            if (!maybeConfigData.Succeeded)
            {
                return maybeConfigData.Failure;
            }

            // Update the cache config to dynamically set the cache path if it is configured to use the per-invocation path.
            // TODO: Ideally this would be exposed as config constructor parameters to BuildXL to not require manipulating the json config.
            //       But for now we just modify the config text before passing it along to the cache.
            string cacheConfigContent = maybeConfigData.Result;

            cacheConfigContent = cacheConfigContent.Replace("[DominoSelectedLogPath]", config.CacheLogFilePath.ToString(pathTable).Replace(@"\", @"\\"));// Escape path separation chars to json format
            cacheConfigContent = cacheConfigContent.Replace("[BuildXLSelectedLogPath]", config.CacheLogFilePath.ToString(pathTable).Replace(@"\", @"\\"));// Escape path separation chars to json format
            cacheConfigContent = cacheConfigContent.Replace("[DominoSelectedRootPath]", cacheDirectory.Replace(@"\", @"\\"));
            cacheConfigContent = cacheConfigContent.Replace("[BuildXLSelectedRootPath]", cacheDirectory.Replace(@"\", @"\\"));
            cacheConfigContent = cacheConfigContent.Replace("[UseDedupStore]", config.UseDedupStore.ToString());

            ICacheConfigData cacheConfigData;
            Exception exception;
            if (!CacheFactory.TryCreateCacheConfigData(cacheConfigContent, out cacheConfigData, out exception))
            {
                return new Failure<string>(I($"Unable to create cache config data: {exception.GetLogEventMessage()}"));
            }

            return new Possible<ICacheConfigData>(cacheConfigData);
        }

        /// <summary>
        /// Asynchronously initializes a cache session instance and corresponding session
        /// </summary>
        /// <returns>
        /// <see cref="Possible"/> <see cref="CacheCoreCacheInitializer"/> that can be used to create a <see cref="EngineCache"/> instance
        /// </returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        internal static async Task<Possible<CacheCoreCacheInitializer>> TryInitializeCacheInternalAsync(
            LoggingContext loggingContext,
            PathTable pathTable,
            string cacheDirectory,
            ICacheConfiguration config,
            bool enableFingerprintLookup,
            RootTranslator rootTranslator)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(pathTable.IsValid);
            Contract.Requires(config != null);
            Contract.Requires(config.CacheLogFilePath.IsValid);
            Contract.Requires(config.CacheConfigFile.IsValid);
            Contract.Requires(!string.IsNullOrWhiteSpace(cacheDirectory));

            bool succeeded = false;
            ICacheCoreCache cache = null;
            ICacheCoreSession session = null;
            try
            {
                Possible<ICacheConfigData> cacheConfigData = TryGetCacheConfigData(pathTable, cacheDirectory, config);
                if (!cacheConfigData.Succeeded)
                {
                    return cacheConfigData.Failure;
                }

                Possible<ICacheCoreCache> maybeCache = await CacheFactory.InitializeCacheAsync(cacheConfigData.Result, loggingContext.ActivityId);

                if (!maybeCache.Succeeded)
                {
                    return maybeCache.Failure;
                }

                // We are now responsible for shutting this down (even if something later fails).
                cache = maybeCache.Result;

                cache.SuscribeForCacheStateDegredationFailures(
                    failure => { Tracing.Logger.Log.CacheReportedRecoverableError(loggingContext, failure.DescribeIncludingInnerFailures()); });

                // Log the cache ID we got.
                Tracing.Logger.Log.CacheInitialized(loggingContext, cache.CacheId);

                Possible<ICacheCoreSession> maybeSession =
                    string.IsNullOrWhiteSpace(config.CacheSessionName)
                        ? await cache.CreateSessionAsync()
                        : await cache.CreateSessionAsync(config.CacheSessionName);

                if (!maybeSession.Succeeded)
                {
                    return maybeSession.Failure;
                }

                session = maybeSession.Result;

                succeeded = true;
                return new CacheCoreCacheInitializer(
                    loggingContext,
                    cache,
                    session,
                    new List<IDisposable>(),
                    enableFingerprintLookup: enableFingerprintLookup,
                    rootTranslator: rootTranslator);
            }
            finally
            {
                if (!succeeded)
                {
                    // Note that we clean up in reverse order that we initialized things.
                    if (session != null)
                    {
                        Analysis.IgnoreResult(await session.CloseAsync(), justification: "Okay to ignore close");
                        Analysis.IgnoreResult(await cache.ShutdownAsync(), justification:  "Okay to ignore shutdown");
                    }
                }
            }
        }

        private static Possible<string> TryReadCacheConfigFile(string path)
        {
            try
            {
                return ExceptionUtilities.HandleRecoverableIOException(
                    () => File.ReadAllText(path),
                    ex => { throw new BuildXLException("Unable to read cache configuration", ex); });
            }
            catch (BuildXLException ex)
            {
                return new RecoverableExceptionFailure(ex);
            }
        }

        /// <summary>
        /// Closes the cache session
        /// </summary>
        public override Possible<string, Failure> Close()
        {
            if (m_session != null)
            {
                return m_session.CloseAsync(LoggingContext.ActivityId).GetAwaiter().GetResult();
            }

            return new Possible<string, Failure>("Success, no session to close.");
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (m_session != null)
            {
                Contract.Assert(m_session.IsClosed, "Cache session must be closed before attempting to dispose the CacheCoreEngineCache.");

                var loggedCacheStatistics = GetLoggedStatistics(m_session, out var cacheMap);
                var cacheStatistics = new Dictionary<string, long>();

                foreach (var statistic in GetLoggedStatistics(m_session, out var cache))
                {
                    cacheStatistics.Add(statistic.qualifiedId, statistic.value);
                }

                Subtract(newStats: cacheStatistics, baseline: m_initialStatistics);

                Logger.Log.CacheBulkStatistics(
                    LoggingContext,
                    cacheStatistics);

                foreach (var statisticsByCacheId in loggedCacheStatistics.GroupBy(s => s.cacheId))
                {
                    Logger.Log.ICacheStatistics(
                        LoggingContext,
                        statisticsByCacheId.Key,
                        cacheMap[statisticsByCacheId.Key],
                        statisticsByCacheId.First().cacheType,
                        statisticsByCacheId.ToDictionary(s => s.id, s => s.value));
                }
            }

            m_cache?.ShutdownAsync().GetAwaiter().GetResult();

            // The base implementation will now dispose anything that was handed to it in startup.
            base.Dispose();
        }

        private static IDictionary<string, long> GetCacheBulkStatistics(ICacheCoreSession session)
        {
            var cacheStatistics = new Dictionary<string, long>();

            foreach (var statistic in GetLoggedStatistics(session, out _))
            {
                cacheStatistics.Add(statistic.qualifiedId, statistic.value);
            }

            return cacheStatistics;
        }

        private static IEnumerable<(string cacheId, string cacheType, string qualifiedId, string id, long value)> GetLoggedStatistics(ICacheCoreSession session, out Dictionary<string, string> cacheNameMap)
        {
            cacheNameMap = null;
            var statistics = new List<(string, string, string, string, long)>();
            var maybeStats = session.GetStatisticsAsync().GetAwaiter().GetResult();
            if (maybeStats.Succeeded)
            {
                var stats = maybeStats.Result;

                cacheNameMap = BuildCacheNameMap(stats);
                foreach (var singleCacheStats in stats)
                {
                    foreach (KeyValuePair<string, double> statValue in singleCacheStats.Statistics)
                    {
                        // The *Sum2 stats are sum of squares of values and while stored as doubles can esaily explode a ulong, so they'll drop here.
                        // If those stats are wanted, they should be collected via an ETW based telemtry system.
                        if (!statValue.Key.EndsWith("Sum2", StringComparison.OrdinalIgnoreCase))
                        {
                            var value = Convert.ToInt64(statValue.Value);
                            statistics.Add((
                                singleCacheStats.CacheId, 
                                singleCacheStats.CacheType, 
                                $"{singleCacheStats.CacheId}.{statValue.Key}", 
                                statValue.Key, 
                                value));
                        }
                    }
                }
            }

            return statistics;
        }

        private static void Subtract(IDictionary<string, long> newStats, IDictionary<string, long> baseline)
        {
            foreach (var key in newStats.Keys.ToList())
            {
                if (baseline.TryGetValue(key, out var oldValue))
                {
                    newStats[key] = newStats[key] - oldValue;
                }
            }
        }

        /// <summary>
        /// Builds a mapping of caches from their hierarchical names to Lx nomenclature.
        /// </summary>
        /// <param name="stats">Stats returned from the cache</param>
        /// <returns>A dictionary mapping cache names to Lx nomenclature.</returns>
        /// <remarks>
        /// This mapping is useful to allow telemetry to flow off the machine in a way that doesn't
        /// explode the Aria pipeline.
        /// </remarks>
        private static Dictionary<string, string> BuildCacheNameMap(CacheSessionStatistics[] stats)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            var longestPathSplit = new string[0];
            int longestPathLength = 0;

            // Find the cache ID with the most '_' characters in it.
            // This will be the cache ID with the deepest hierarchy.
            foreach (CacheSessionStatistics oneStatSet in stats)
            {
                string[] splitPath = oneStatSet.CacheId.Split('_');
                if (splitPath.Length > longestPathLength)
                {
                    longestPathLength = splitPath.Length;
                    longestPathSplit = splitPath;
                }
            }

            // Now set the cache IDs for all caches that are aggregators
            foreach (CacheSessionStatistics oneStatSet in stats)
            {
                string[] splitPath = oneStatSet.CacheId.Split('_');

                if (splitPath.Length > 1)
                {
                    int baseLevel = longestPathLength - splitPath.Length + 1;
                    string cacheKey = I($"L{baseLevel}_L{baseLevel + 1}");

                    result.Add(oneStatSet.CacheId, cacheKey);
                }
            }

            // Set the cache names for all caches that are single level
            for (int i = 1; i <= longestPathSplit.Length; i++)
            {
                string key = longestPathSplit[i - 1];
                if (!result.ContainsKey(key))
                {
                    result.Add(key, I($"L{i}"));
                }
                else
                {
                    throw new InvalidOperationException($"Cannot add cache name key {key}=L{i} to cache name table as the key already exists. " +
                        $"Existing entries: {(string.Join(",", result.Select(kvp => kvp.Key + "=" + kvp.Value)))}");
                }
            }

            return result;
        }

        internal override void LogStats(LoggingContext context)
        {
        }
    }

    /// <summary>
    /// 'await'-able wrapper for async cache initialization. This wrapper exists to facilitate logging;
    /// the first await time is captured for logging (useful to compare with init start and stop time).
    /// </summary>
    public sealed class CacheInitializationTask : IDisposable
    {
        private readonly LoggingContext m_loggingContext;
        private readonly DateTime m_initializationStart;
        private readonly Task<Possible<CacheInitializer>> m_initializationTask;
        private readonly Timer m_cacheInitWatchdog;

        private long m_firstAwaitTimeTicks = -1;

        /// <summary>
        /// The time the cache spent initializing. This will be zero until the cache actually finished initialization
        /// </summary>
        public TimeSpan InitializationTime { get; private set; }

        /// <nodoc />
        internal CacheInitializationTask(
            LoggingContext loggingContext,
            DateTime initializationStart,
            Task<Possible<CacheInitializer>> initializationTask,
            CancellationToken cancellationToken)
        {
            m_loggingContext = loggingContext;
            m_initializationStart = initializationStart;

            // initializationTask might be done already; safe to call ContinueWith now since we initialized everything else.
            m_initializationTask = initializationTask.ContinueWith(
                t =>
                {
                    if (t.IsCanceled)
                    {
                        return new Failure<string>("Cache initialization has been cancelled");
                    }

                    // Abstractly, there are two orders possible:
                    // {m_initializationStart -> [firstAwaited} -> completionTime] (some sync wait time)
                    // {m_initializationStart -> completionTime} -> firstAwaited (no sync wait time)
                    // [ ] part is sync wait time - in the second case it is zero (timeWaitedMs below)
                    // { } part was overlapped with other processing (i.e., initialization before first wait).
                    // Since we arrive here at task completion time, we only care about the first case;
                    // if an await hasn't happened by completionTime, we pretend that firstAwaited == completionTime.
                    DateTime completionTime = DateTime.UtcNow;

                    long firstWaitTimeTicksOrNegativeOne = Volatile.Read(ref m_firstAwaitTimeTicks);
                    DateTime firstAwaitTime = firstWaitTimeTicksOrNegativeOne == -1
                        ? completionTime
                        : new DateTime(firstWaitTimeTicksOrNegativeOne, DateTimeKind.Utc);

                    if (firstAwaitTime > completionTime)
                    {
                        firstAwaitTime = completionTime;
                    }

                    // If an await hasn't happened yet, timeWaitedMs is zero (completionTime == firstAwaitTime; see above)
                    int timeWaitedMs = (int)Math.Round(Math.Max(0, (completionTime - firstAwaitTime).TotalMilliseconds));
                    Contract.Assert(timeWaitedMs >= 0);

                    InitializationTime = completionTime - m_initializationStart;
                    if (InitializationTime < TimeSpan.Zero)
                    {
                        InitializationTime = TimeSpan.Zero;
                    }

                    int overlappedInitializationMs = (int)Math.Round(Math.Max(0, InitializationTime.TotalMilliseconds - timeWaitedMs));

                    Tracing.Logger.Log.SynchronouslyWaitedForCache(loggingContext, timeWaitedMs, overlappedInitializationMs);

                    LoggingHelpers.LogCategorizedStatistic(m_loggingContext, "CacheInitialization", "TimeWaitedMs", timeWaitedMs);
                    LoggingHelpers.LogCategorizedStatistic(m_loggingContext, "CacheInitialization", "OverlappedInitializationMs", overlappedInitializationMs);
                    return t.Result;
                });

            // Timer will start if someone actually waiting on the task (called GetAwaiter()).
            m_cacheInitWatchdog = new Timer(o => CheckIfCacheIsStillInitializing(cancellationToken));
        }

        private void CheckIfCacheIsStillInitializing(CancellationToken cancellationToken)
        {
            if (!m_initializationTask.IsCompleted)
            {
                Tracing.Logger.Log.CacheIsStillBeingInitialized(m_loggingContext);
            }
        }

        /// <summary>
        /// Returns a task representation
        /// </summary>
        public async Task<Possible<CacheInitializer>> AsTask()
        {
            // Need to justify using async here to the compiler
            await BuildXL.Utilities.Tasks.BoolTask.False;
            return await this;
        }

        /// <summary>
        /// Gets an awaiter for the underlying initialization task. This is the pattern-based interface as required by the 'await' keyword.
        /// In addition to returning the underlying awaiter, this tracks the first await time for logging (useful to compare with init start time).
        /// </summary>
        public TaskAwaiter<Possible<CacheInitializer>> GetAwaiter()
        {
            long nowTicks = DateTime.UtcNow.Ticks;

            // Starting the cache watchdog, it will signal if cache takes a lot of time to initialize.
            m_cacheInitWatchdog.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5));

            // It's possible multiple threads are waiting on initialization to finish at the same time. We want to remember
            // only the earliest await time.
            Analysis.IgnoreResult(Interlocked.CompareExchange(ref m_firstAwaitTimeTicks, nowTicks, comparand: -1));

            return m_initializationTask.GetAwaiter();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_initializationTask?.Dispose();
            m_cacheInitWatchdog.Dispose();
        }
    }
}
