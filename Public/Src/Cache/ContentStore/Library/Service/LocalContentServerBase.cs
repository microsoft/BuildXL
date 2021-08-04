// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Timers;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using Grpc.Core;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Cache.Host.Service;
using GrpcEnvironment = BuildXL.Cache.ContentStore.Service.Grpc.GrpcEnvironment;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Utilities.Tasks;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service
{
    /// <nodoc />
    public interface ILocalContentServer<TStore> : IStartupShutdown
    {
        /// <nodoc />
        IReadOnlyDictionary<string, TStore> StoresByName { get; }
    }

    /// <summary>
    /// Base implementation of IPC to a file system content cache.
    /// </summary>
    /// <typeparam name="TStore">
    ///     Type of underlying store. This is kept to inherit from <see cref="IStartupShutdown"/> instead of, for
    ///     example, <see cref="IContentStore"/>, because if that were the case it couldn't be used with ICache
    /// </typeparam>
    /// <typeparam name="TSession">
    ///     Type of sessions that will be created. Must match the store.
    /// </typeparam>
    /// <typeparam name="TSessionData">
    ///     Type of data associated with sessions.
    /// </typeparam>
    public abstract class LocalContentServerBase<TStore, TSession, TSessionData> : StartupShutdownBase, ISessionHandler<TSession, TSessionData>, ILocalContentServer<TStore>, IServicesProvider
        where TSession : IContentSession
        where TStore : IStartupShutdown
        where TSessionData : ISessionData
    {
        private const string Name = nameof(LocalContentServerBase<TStore, TSession, TSessionData>);
        private const string CheckForExpiredSessionsName = "CheckUnusedSessions";
        private const int CheckForExpiredSessionsPeriodMinutes = 1;

        private readonly IDisposable? _portDisposer; // Null if port should not be exposed.
        private Server? _grpcServer;
        private readonly IGrpcServiceEndpoint[] _additionalEndpoints;

        private readonly ServiceReadinessChecker _serviceReadinessChecker;

        private readonly ConcurrentDictionary<int, ISessionHandle<TSession, TSessionData>> _sessionHandles;
        private IntervalTimer? _sessionExpirationCheckTimer;
        private IntervalTimer? _logIncrementalStatsTimer;
        private IntervalTimer? _logMachineStatsTimer;

        private Dictionary<string, long>? _previousStatistics;

        private readonly MachinePerformanceCollector _performanceCollector = new MachinePerformanceCollector();

        private readonly Dictionary<string, AbsolutePath> _tempFolderForStreamsByCacheName = new Dictionary<string, AbsolutePath>();
        private readonly ConcurrentDictionary<int, DisposableDirectory> _tempDirectoryForStreamsBySessionId = new ConcurrentDictionary<int, DisposableDirectory>();
        private readonly Dictionary<string, bool> _incrementalStatisticsKeyStatus = new Dictionary<string, bool>();

        /// <summary>
        /// Used by <see cref="LogIncrementalStatsAsync"/> to avoid re-entrancy.
        /// </summary>
        private int _loggingIncrementalStats = 0;
        private int _lastSessionId;

        /// <nodoc />
        protected readonly LocalServerConfiguration Config;

        /// <nodoc />
        protected readonly IAbsFileSystem FileSystem;

        /// <nodoc />
        protected readonly ILogger Logger;

        /// <nodoc />
        protected abstract ICacheServerServices Services { get; }

        /// <summary>
        /// Collection of stores by name.
        /// </summary>
        public IReadOnlyDictionary<string, TStore> StoresByName { get; }

        /// <nodoc />
        protected LocalContentServerBase(
            ILogger logger,
            IAbsFileSystem fileSystem,
            string scenario,
            Func<AbsolutePath, TStore> contentStoreFactory,
            LocalServerConfiguration localContentServerConfiguration,
            IGrpcServiceEndpoint[]? additionalEndpoints)
        {
            Contract.Requires(logger != null);
            Contract.Requires(fileSystem != null);
            Contract.Requires(localContentServerConfiguration != null);
            Contract.Requires(localContentServerConfiguration.GrpcPort > 0, "GrpcPort must be provided");

            logger.Debug($"{Name} process id {Process.GetCurrentProcess().Id}");
            logger.Debug($"{Name} constructing {nameof(ServiceConfiguration)}: {localContentServerConfiguration}");

            GrpcEnvironment.Initialize(logger, localContentServerConfiguration.GrpcEnvironmentOptions, overwriteSafeOptions: true);

            FileSystem = fileSystem;
            Logger = logger;
            Config = localContentServerConfiguration;

            _additionalEndpoints = additionalEndpoints ?? Array.Empty<IGrpcServiceEndpoint>();
            _serviceReadinessChecker = new ServiceReadinessChecker(logger, scenario);
            _sessionHandles = new ConcurrentDictionary<int, ISessionHandle<TSession, TSessionData>>();

            var storesByName = new Dictionary<string, TStore>();
            foreach (var kvp in localContentServerConfiguration.NamedCacheRoots)
            {
                fileSystem.CreateDirectory(kvp.Value);
                var store = contentStoreFactory(kvp.Value);
                storesByName.Add(kvp.Key, store);
            }
            StoresByName = new ReadOnlyDictionary<string, TStore>(storesByName);

            foreach (var kvp in localContentServerConfiguration.NamedCacheRoots)
            {
                _tempFolderForStreamsByCacheName[kvp.Key] = kvp.Value / "TempFolder";
            }

            if (!string.IsNullOrEmpty(localContentServerConfiguration.GrpcPortFileName))
            {
                var portSharingFactory = new MemoryMappedFileGrpcPortSharingFactory(logger, localContentServerConfiguration.GrpcPortFileName);
                var portExposer = portSharingFactory.GetPortExposer();
                _portDisposer = portExposer.Expose(localContentServerConfiguration.GrpcPort);
            }
        }

        /// <summary>
        /// Get list of current session IDs.
        /// </summary>
        public IReadOnlyList<int> GetSessionIds()
        {
            return _sessionHandles.Select(h => h.Key).ToList();
        }

        /// <summary>
        /// Gets an array of service definitions that will be exposed via grpc server.
        /// </summary>
        /// <returns></returns>
        protected abstract ServerServiceDefinition[] BindServices();

        /// <nodoc />
        protected abstract Task<GetStatsResult> GetStatsAsync(TStore store, OperationContext context);

        /// <nodoc />
        protected abstract CreateSessionResult<TSession> CreateSession(TStore store, OperationContext context, TSessionData sessionData);

        private async Task<BoolResult> RemoveFromTrackerAsync(TStore store, OperationContext context, string storeName)
        {
            if (store is IRepairStore repairStore)
            {
                var result = await repairStore.RemoveFromTrackerAsync(context);
                if (!result)
                {
                    return result;
                }
            }

            Logger.Debug($"Repair handling not enabled for {storeName}'s content store.");
            return BoolResult.Success;
        }

        /// <inheritdoc />
        public async Task<Result<CounterSet>> GetStatsAsync(OperationContext context)
        {
            var counterSet = new CounterSet();

            foreach (var (name, store) in StoresByName)
            {
                var stats = await GetStatsAsync(store, context);
                if (!stats)
                {
                    return Result.FromError<CounterSet>(stats);
                }

                counterSet.Merge(stats.CounterSet, $"{name}.");
            }

            return counterSet;
        }

        /// <inheritdoc />
        public async Task<BoolResult> RemoveFromTrackerAsync(OperationContext context)
        {
            foreach (var (name, store) in StoresByName)
            {
                var result = await RemoveFromTrackerAsync(store, context, name);
                if (!result)
                {
                    return result;
                }
            }

            return BoolResult.Success;
        }

        private async Task<Result<AbsolutePath>> CreateSessionTempDirectoryAsync(OperationContext context, string cacheName, int sessionId)
        {
            if (!_tempFolderForStreamsByCacheName.TryGetValue(cacheName, out var tempDirectoryRoot))
            {
                await ReleaseSessionAsync(context, sessionId);
                return Result.FromErrorMessage<AbsolutePath>("Failed to get temp directory for cache name");
            }
            else
            {
                Tracer.Debug(context, $"{Name} creating temporary directory for session {sessionId.AsTraceableSessionId()}.");
                var disposableDirectory = _tempDirectoryForStreamsBySessionId.GetOrAdd(
                    sessionId,
                    (_) => new DisposableDirectory(FileSystem, tempDirectoryRoot / sessionId.ToString()));
                return Result.Success(disposableDirectory.Path);
            }
        }

        private void RemoveSessionTempDirectory(int sessionId)
        {
            if (_tempDirectoryForStreamsBySessionId.TryRemove(sessionId, out var disposableDirectory))
            {
                disposableDirectory.Dispose();
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            // Splitting initialization into two pieces:
            // Normal startup procedure and post-initialization step that notifies all
            // the special stores that the initialization has finished.
            // This is a workaround to make sure hibernated sessions are fully restored
            // before FileSystemContentStore can evict the content.
            var result = await tryStartupCoreAsync();
            if (!result)
            {
                // We should not be running post initialization operation if the startup operation failed.
                return result;
            }

            foreach (var store in StoresByName.Values)
            {
                if (store is IContentStore contentStore)
                {
                    contentStore.PostInitializationCompleted(context, result);
                }
            }

            return result;

            async Task<BoolResult> tryStartupCoreAsync()
            {
                try
                {
                    if (!FileSystem.DirectoryExists(Config.DataRootPath))
                    {
                        FileSystem.CreateDirectory(Config.DataRootPath);
                    }

                    await StartupStoresAsync(context).ThrowIfFailure();

                    foreach (var endpoint in _additionalEndpoints)
                    {
                        await endpoint.StartupAsync(context).ThrowIfFailure();
                    }

                    await LoadHibernatedSessionsAsync(context);

                    InitializeAndStartGrpcServer(Config.GrpcPort, BindServices(), Config.RequestCallTokensPerCompletionQueue, Config.GrpcCoreServerOptions);

                    _serviceReadinessChecker.Ready(context);

                    _sessionExpirationCheckTimer = new IntervalTimer(
                        () => CheckForExpiredSessionsAsync(context),
                        MinTimeSpan(Config.UnusedSessionHeartbeatTimeout, TimeSpan.FromMinutes(CheckForExpiredSessionsPeriodMinutes)),
                        message => Tracer.Debug(context, $"[{CheckForExpiredSessionsName}] message"));

                    _logIncrementalStatsTimer = new IntervalTimer(
                        () => LogIncrementalStatsAsync(context, logAtShutdown: false),
                        Config.LogIncrementalStatsInterval);

                    _logMachineStatsTimer = new IntervalTimer(
                        () => LogMachinePerformanceStatistics(context),
                        Config.LogMachineStatsInterval);

                    return BoolResult.Success;
                }
                catch (Exception e)
                {
                    return new BoolResult(e);
                }
            }
        }

        private void InitializeAndStartGrpcServer(int grpcPort, ServerServiceDefinition[] definitions, int requestCallTokensPerCompletionQueue, GrpcCoreServerOptions? grpcCoreServerOptions)
        {
            Contract.Requires(definitions.Length != 0);

            GrpcEnvironment.WaitUntilInitialized();
            _grpcServer = new Server(GrpcEnvironment.GetServerOptions(grpcCoreServerOptions))
            {
                Ports = { new ServerPort(IPAddress.Any.ToString(), grpcPort, ServerCredentials.Insecure) },
                RequestCallTokensPerCompletionQueue = requestCallTokensPerCompletionQueue,
            };

            foreach (var endpoint in _additionalEndpoints)
            {
                endpoint.BindServices(_grpcServer.Services);
            }

            foreach (var definition in definitions)
            {
                _grpcServer.Services.Add(definition);
            }

            _grpcServer.Start();
        }

        private Task LogIncrementalStatsAsync(OperationContext context, bool logAtShutdown)
        {
            return ConcurrencyHelper.RunOnceIfNeeded(
                ref _loggingIncrementalStats,
                async () =>
                {
                    TraceLeakedFilePath(context);

                    var statistics = new Dictionary<string, long>();
                    var previousStatistics = _previousStatistics;

                    var stats = await GetStatsAsync(context);
                    if (stats.Succeeded)
                    {
                        var counters = stats.Value.ToDictionaryIntegral();
                        FillTrackingStreamStatistics(counters);
                        foreach (var counter in counters)
                        {
                            var key = counter.Key;

                            if (!logAtShutdown && !PrintStatisticsForKey(key))
                            {
                                continue;
                            }

                            var value = counter.Value;
                            var incrementalValue = value;
                            statistics[key] = value;

                            if (previousStatistics != null && previousStatistics.TryGetValue(key, out var oldValue))
                            {
                                incrementalValue -= oldValue;
                            }

                            context.TracingContext.TraceMessage(
                                Severity.Info,
                                $"{key}=[{incrementalValue}]",
                                component: Name,
                                operation: "IncrementalStatistics");
                            context.TracingContext.TraceMessage(Severity.Info, $"{key}=[{value}]", component: Name, operation: "PeriodicStatistics");
                        }
                    }

                    _previousStatistics = statistics;
                },
                funcIsRunningResultProvider: () => Task.CompletedTask);
        }

        private bool PrintStatisticsForKey(string key)
        {
            if (_incrementalStatisticsKeyStatus.TryGetValue(key, out bool shouldPrint))
            {
                return shouldPrint;
            }

            shouldPrint = Config.IncrementalStatsCounterNames.Any(name => key.EndsWith(name));
            _incrementalStatisticsKeyStatus[key] = shouldPrint;
            return shouldPrint;
        }

        private void TraceLeakedFilePath(OperationContext context)
        {
            // Tracing the last leaked file name to understand what files are not closed properly.
            var leakedPath = TrackingFileStream.LastLeakedFilePath;
            if (!string.IsNullOrEmpty(leakedPath))
            {
                Tracer.Warning(context, $"{nameof(TrackingFileStream)}.{nameof(TrackingFileStream.LastLeakedFilePath)}: {leakedPath}");
            }
        }

        private void LogMachinePerformanceStatistics(OperationContext context)
        {
            var machineStatistics = _performanceCollector.GetMachinePerformanceStatistics();
            Tracer.Info(context, "MachinePerformanceStatistics: " + machineStatistics);
        }

        private static void FillTrackingStreamStatistics(IDictionary<string, long> statistics)
        {
            // This method fills up counters for tracking memory leaks with file streams.
            statistics[$"{nameof(TrackingFileStream)}.{nameof(TrackingFileStream.Constructed)}"] = Interlocked.Read(ref TrackingFileStream.Constructed);
            statistics[$"{nameof(TrackingFileStream)}.{nameof(TrackingFileStream.ProperlyClosed)}"] = Interlocked.Read(ref TrackingFileStream.ProperlyClosed);
            statistics[$"{nameof(TrackingFileStream)}.{nameof(TrackingFileStream.Leaked)}"] = TrackingFileStream.Leaked;
        }

        private async Task CheckForExpiredSessionsAsync(Context context)
        {
            foreach (int sessionId in _sessionHandles.Keys)
            {
                if (_sessionHandles.TryGetValue(sessionId, out var sessionHandle))
                {
                    Contract.Assert(sessionHandle != null);
                    if (sessionHandle.SessionExpirationUtcTicks < DateTime.UtcNow.Ticks)
                    {
                        if (Config.DoNotShutdownSessionsInUse && sessionHandle.CurrentUsageCount > 0)
                        {
                            Tracer.Debug(
                                context,
                                $"Bump the expiry for session because its being used. {DescribeSession(sessionId, sessionHandle)}");
                            sessionHandle.BumpExpiration();
                        }
                        else
                        {
                            Tracer.Debug(
                                context,
                                $"Releasing session because of expiry. {DescribeSession(sessionId, sessionHandle)}");
                            await ReleaseSessionInternalAsync(context, sessionId);
                        }
                    }
                }
            }
        }

        private TimeSpan MinTimeSpan(TimeSpan ts1, TimeSpan ts2)
        {
            return ts1 < ts2 ? ts1 : ts2;
        }

        private async Task<BoolResult> StartupStoresAsync(Context context)
        {
            var tasks = new List<Task<BoolResult>>(StoresByName.Count);
            tasks.AddRange(StoresByName.Select(kvp => kvp.Value.StartupAsync(context)));
            await TaskUtilities.SafeWhenAll(tasks);

            var result = BoolResult.Success;
            foreach (var task in tasks)
            {
                var r = await task;
                if (result.Succeeded && !r.Succeeded)
                {
                    result = r;
                }
            }

            return result;
        }

        /// <summary>
        /// Gets information for sessions that were hibernated in a previous run.
        /// </summary>
        protected abstract Task<IReadOnlyList<HibernatedSessionInfo>> RestoreHibernatedSessionDatasAsync(OperationContext context);

        /// <summary>
        /// Determines whether sessions were hibernated in a previous run.
        /// </summary>
        protected abstract bool HibernatedSessionsExist();

        /// <summary>
        /// Cleans up files related to previous hibernations, as to not restore them in future runs.
        /// </summary>
        protected abstract Task CleanupHibernatedSessions();

        /// <summary>
        /// Performs initialization tasks for sessions that were just restored via hibernation.
        /// </summary>
        protected abstract Task RestoreHibernatedSessionStateAsync(OperationContext context, TSession session, TSessionData sessionData);

        /// <summary>
        /// Persists the set of currently running sessions so that future runs can restore them.
        /// </summary>
        protected abstract Task HibernateSessionsAsync(Context context, IDictionary<int, ISessionHandle<TSession, TSessionData>> sessionHandles);

        private async Task LoadHibernatedSessionsAsync(OperationContext context)
        {
            try
            {
                if (HibernatedSessionsExist())
                {
                    try
                    {
                        var stopWatch = Stopwatch.StartNew();
                        var sessionDatas = await RestoreHibernatedSessionDatasAsync(context);
                        stopWatch.Stop();
                        Tracer.Debug(
                            context, $"Read hibernated sessions from root=[{Config.DataRootPath}] in {stopWatch.Elapsed.TotalMilliseconds}ms");

                        if (sessionDatas.Any())
                        {
                            foreach (var sessionInfo in sessionDatas)
                            {
                                Tracer.Debug(context, $"Restoring hibernated session {DescribeHibernatedSessionInfo(sessionInfo)}.");

                                // If there was no expiration stored, then default to the longer timeout
                                // Otherwise, default to at least the shorter timeout
                                var newExpirationTicks = sessionInfo.ExpirationUtcTicks == default(long)
                                    ? (DateTime.UtcNow + Config.UnusedSessionTimeout).Ticks
                                    : Math.Max(sessionInfo.ExpirationUtcTicks, (DateTime.UtcNow + Config.UnusedSessionHeartbeatTimeout).Ticks);

                                var sessionResult = await CreateTempDirectoryAndSessionAsync(
                                    context,
                                    sessionInfo.SessionData,
                                    sessionInfo.Id,
                                    sessionInfo.CacheName,
                                    newExpirationTicks);

                                if (sessionResult.Succeeded)
                                {
                                    await RestoreHibernatedSessionStateAsync(context, sessionResult.Value.session, sessionInfo.SessionData);
                                }
                                else
                                {
                                    Tracer.Warning(context, $"Failed to restore hibernated session, error=[{sessionResult.ErrorMessage}]. {DescribeHibernatedSessionInfo(sessionInfo)}");
                                }
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Tracer.Error(context, exception, $"Failed to read hibernated sessions root=[{Config.DataRootPath}]");
                    }
                    finally
                    {
                        // Done reading hibernated sessions. No matter what happened, remove the file so we don't attempt to load later.
                        Tracer.Debug(context, $"Deleting hibernated sessions from root=[{Config.DataRootPath}].");
                        await CleanupHibernatedSessions();
                    }

                    _lastSessionId = _sessionHandles.Any() ? _sessionHandles.Keys.Max() : 0;
                }
            }
            catch (Exception exception)
            {
                Tracer.Error(context, exception, "Failed to load hibernated sessions", operation: nameof(LoadHibernatedSessionsAsync));
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _serviceReadinessChecker.Reset();

            _portDisposer?.Dispose();

            if (_grpcServer != null)
            {
                await _grpcServer.KillAsync();
            }

            var success = BoolResult.Success;

            foreach (var endpoint in _additionalEndpoints)
            {
                success &= await endpoint.ShutdownAsync(context);
            }

            _logIncrementalStatsTimer?.Dispose();
            _logMachineStatsTimer?.Dispose();

            // Don't trace statistics if configured and only if startup was successful.
            if (Tracer.EnableTraceStatisticsAtShutdown && StartupCompleted)
            {
                await LogIncrementalStatsAsync(context, logAtShutdown: false);
            }

            // Stop the session expiration timer.
            _sessionExpirationCheckTimer?.Dispose();

            // Hibernate dangling sessions in case we are shutdown unexpectedly to live clients.
            await HandleShutdownDanglingSessionsAsync(context);

            // Cleaning up all the temp directories associated with sessions (they'll be recreated during the next startup when hibernated sessions are recreated).
            CleanSessionTempDirectories(context);

            // Now the stores, without active users, can be shut down.
            return success & await ShutdownStoresAsync(context);
        }

        private async Task HandleShutdownDanglingSessionsAsync(Context context)
        {
            int sessionCount = 0;
            // Using PerformOperationAsync to trace errors in a consistent manner.
            (await new OperationContext(context)
                .PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        sessionCount = _sessionHandles.Count;
                        if (sessionCount != 0)
                        {
                            await HibernateSessionsAsync(context, _sessionHandles);

                            _sessionHandles.Clear();
                        }

                        return BoolResult.Success;
                    },
                    extraEndMessage: _ => $"Processed {sessionCount} sessions"))
                .IgnoreFailure(); // Can ignore failures because the error (if occurred) is already traced.
        }

        private async Task<BoolResult> ShutdownStoresAsync(Context context)
        {
            var tasks = new List<Task<BoolResult>>(StoresByName.Count);
            tasks.AddRange(StoresByName.Select(kvp => kvp.Value.ShutdownIfStartedAsync(context)));
            await TaskUtilities.SafeWhenAll(tasks);

            var result = BoolResult.Success;
            foreach (var task in tasks)
            {
                var r = await task;
                if (!result.Succeeded && r.Succeeded)
                {
                    result = r;
                }
            }

            return result;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            base.DisposeCore();

            Logger.Debug($"{Name} disposing");

            _portDisposer?.Dispose();

            foreach (var storeAndCount in StoresByName.Values)
            {
                storeAndCount.Dispose();
            }

            _sessionExpirationCheckTimer?.Dispose();
            _logIncrementalStatsTimer?.Dispose();
            _logMachineStatsTimer?.Dispose();

            _serviceReadinessChecker.Dispose();

            Logger.Debug($"{Name} disposed");
        }

        private void CleanSessionTempDirectories(OperationContext context)
        {
            int count = 0;
            Tracer.Debug(context, $"{Name} cleaning up session's temp directories.");
            foreach (var tempDirectory in _tempDirectoryForStreamsBySessionId.Values)
            {
                count++;
                tempDirectory.Dispose();
            }

            _tempDirectoryForStreamsBySessionId.Clear();

            Tracer.Debug(context, $"{Name} cleaned {count} session's temp directories.");
        }

        private Task<Result<TSession>> CreateSessionAsync(
            OperationContext context,
            TSessionData sessionData,
            string cacheName,
            int id,
            long sessionExpirationUtcTicks)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    TrySetBuildId(sessionData.Name);
                    if (!StoresByName.TryGetValue(cacheName, out var store))
                    {
                        return Result.FromErrorMessage<TSession>($"Cache by name=[{cacheName}] is not available");
                    }

                    var result = CreateSession(store, context, sessionData).ThrowIfFailure();

                    var session = result.Session!; // Still need to use '!' here because the compiler doesn't know the semantics of 'ThrowIfFailure'.
                    await session.StartupAsync(context).ThrowIfFailure();

                    var handle = new SessionHandle<TSession, TSessionData>(
                        session,
                        sessionData,
                        cacheName,
                        sessionExpirationUtcTicks,
                        GetTimeoutForCapabilities(sessionData.Capabilities));

                    bool added = _sessionHandles.TryAdd(id, handle);
                    Contract.Check(added)?.Assert($"The session with id '{id}' is already created.");
                    Tracer.Debug(context, $"{nameof(CreateSessionAsync)} created session {handle.ToString(id)}.");
                    return Result.Success(session);
                });
        }

        private void TrySetBuildId(string sessionName)
        {
            // Domino provides build ID through session name for CB builds.
            if (Logger is IOperationLogger && Constants.TryExtractBuildId(sessionName, out var buildId))
            {
                Logger.RegisterBuildId(buildId);
            }
        }

        private void TryUnsetBuildId(string sessionName)
        {
            // Domino provides build ID through session name for CB builds.
            if (Logger is IOperationLogger && Constants.TryExtractBuildId(sessionName, out _))
            {
                Logger.UnregisterBuildId();
            }
        }

        /// <inheritdoc />
        public ISessionReference<TSession>? GetSession(int sessionId)
        {
            if (_sessionHandles.TryGetValue(sessionId, out var sessionHandle))
            {
                sessionHandle.BumpExpiration();
                return new SessionReference<TSession>(sessionHandle.Session, sessionHandle);
            }

            return null;
        }

        private Task<Result<(TSession session, int sessionId, AbsolutePath? tempDirectory)>> CreateTempDirectoryAndSessionAsync(
            OperationContext context,
            TSessionData sessionData,
            int? sessionIdHint,
            string cacheName,
            long sessionExpirationUtcTicks)
        {
            // The hint is provided when the session is recovered from hibernation.
            var sessionId = sessionIdHint ?? Interlocked.Increment(ref _lastSessionId);

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var tempDirectoryCreationResult = await CreateSessionTempDirectoryAsync(context, cacheName, sessionId);

                    if (!tempDirectoryCreationResult.Succeeded)
                    {
                        return new Result<(TSession session, int sessionId, AbsolutePath? tempDirectory)>(tempDirectoryCreationResult);
                    }

                    var sessionResult = await CreateSessionAsync(
                        context,
                        sessionData,
                        cacheName,
                        sessionId,
                        sessionExpirationUtcTicks);

                    if (!sessionResult.Succeeded)
                    {
                        RemoveSessionTempDirectory(sessionId);
                        return Result.FromError<(TSession session, int sessionId, AbsolutePath? tempDirectory)>(sessionResult);
                    }

                    return Result.Success<(TSession session, int sessionId, AbsolutePath? tempDirectory)>(
                        (sessionResult.Value, sessionId, tempDirectoryCreationResult.Value));
                },
                extraStartMessage: sessionId.AsTraceableSessionId(),
                extraEndMessage: r => sessionId.AsTraceableSessionId());
        }

        /// <inheritdoc />
        public async Task<Result<(int sessionId, AbsolutePath? tempDirectory)>> CreateSessionAsync(
            OperationContext context,
            TSessionData sessionData,
            string cacheName)
        {
            cacheName ??= _tempFolderForStreamsByCacheName.Keys.First();

            var result = await CreateTempDirectoryAndSessionAsync(
                context,
                sessionData,
                sessionIdHint: null, // SessionId must be recreated for new sessions.
                cacheName,
                (DateTime.UtcNow + GetTimeoutForCapabilities(sessionData.Capabilities)).Ticks);

            if (!result)
            {
                return new Result<(int sessionId, AbsolutePath? tempDirectory)>(result);
            }

            return Result.Success((result.Value.sessionId, result.Value.tempDirectory));
        }

        private TimeSpan GetTimeoutForCapabilities(Capabilities capabilities)
        {
            return (capabilities & Capabilities.Heartbeat) != 0 ? Config.UnusedSessionHeartbeatTimeout : Config.UnusedSessionTimeout;
        }

        /// <inheritdoc />
        public Task ReleaseSessionAsync(OperationContext context, int sessionId)
        {
            return ReleaseSessionInternalAsync(context, sessionId);
        }

        private async Task ReleaseSessionInternalAsync(Context context, int sessionId)
        {
            RemoveSessionTempDirectory(sessionId);

            string method = nameof(ISessionHandler<TSession, TSessionData>.ReleaseSessionAsync);
            if (sessionId < 0)
            {
                return;
            }

            if (!_sessionHandles.TryGetValue(sessionId, out var sessionHandle))
            {
                Tracer.Warning(context, $"{method} failed to lookup session by Id. {sessionId.AsTraceableSessionId()}");
                return;
            }

            if (!_sessionHandles.TryRemove(sessionId, out _))
            {
                Tracer.Warning(context, $"{method} failed to remove entry for session by Id. {sessionId.AsTraceableSessionId()}");
                return;
            }

            if (sessionHandle.Session is IAsyncShutdown blockingSession)
            {
                // We need to make sure that we don't block the client from shutting down.
                Tracer.Debug(context, $"{method} closing session by requesting an async shutdown. {DescribeSession(sessionId, sessionHandle)}.");
                requestAsyncShutdown().FireAndForget(context);
                return;
            }

            Tracer.Debug(context, $"{method} closing session. {DescribeSession(sessionId, sessionHandle)}");
            await sessionHandle.Session.ShutdownAsync(context).ThrowIfFailure();
            disposeSessionHandle();

            async Task<BoolResult> requestAsyncShutdown()
            {
                try
                {
                    // Do not remove async/await and try to return the task: it will execute the 'finally' block!
                    return await blockingSession.RequestShutdownAsync(context).WithTimeoutAsync(Config.AsyncSessionShutdownTimeout);
                }
                catch (Exception e)
                {
                    Tracer.Error(context, e, message: "Threw an exception during async shutdown. Attempting regular shutdown.");
                    return await sessionHandle.Session.ShutdownAsync(context);
                }
                finally
                {
                    disposeSessionHandle();
                }
            }

            void disposeSessionHandle()
            {
                TryUnsetBuildId(sessionHandle.SessionData.Name);
                sessionHandle.Session.Dispose();
            }
        }

        private string DescribeSession(int id, ISessionHandle<TSession, TSessionData> handle) => handle.ToString(id);

        private string DescribeHibernatedSessionInfo(HibernatedSessionInfo info)
        {
            var expirationDateTime = new DateTime(info.ExpirationUtcTicks).ToLocalTime();
            return $"{info.Id.AsTraceableSessionId()} Name=[{info.SessionData.Name}] Expiration=[{expirationDateTime}] Capabilities=[{info.SessionData.Capabilities}] Pins=[{info.PinCount}]";
        }

        /// <inheritdoc />
        public bool TryGetService<TService>(out TService? service)
        {
            if (Services is TService typedService)
            {
                service = typedService;
                return true;
            }

            service = default;
            return false;
        }

        /// <summary>
        /// Intended for testing only. Returns the current set of session and their corresponding datas.
        /// </summary>
        public (TSession session, TSessionData data)[] GetCurrentSessions() => _sessionHandles.Values.Select(handle => (handle.Session, handle.SessionData)).ToArray();

        /// <summary>
        /// Information related to a session that was hibernated in a previous run.
        /// </summary>
        public class HibernatedSessionInfo
        {
            /// <nodoc />
            public int Id { get; }

            /// <nodoc />
            public string CacheName { get; }

            /// <nodoc />
            public long ExpirationUtcTicks { get; }

            /// <nodoc />
            public TSessionData SessionData { get; }

            /// <nodoc />
            public int PinCount { get; }

            /// <nodoc />
            public HibernatedSessionInfo(int id, string cacheName, long expirationUtcTicks, TSessionData sessionData, int pinCount)
            {
                Id = id;
                CacheName = cacheName;
                ExpirationUtcTicks = expirationUtcTicks;
                SessionData = sessionData;
                PinCount = pinCount;
            }
        }
    }
}
