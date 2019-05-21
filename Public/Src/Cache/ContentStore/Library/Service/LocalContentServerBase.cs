// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Timers;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using Grpc.Core;
using GrpcEnvironment = BuildXL.Cache.ContentStore.Service.Grpc.GrpcEnvironment;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Base implementation of IPC to a file system content cache.
    /// </summary>
    public abstract class LocalContentServerBase<TStore, TSession> : StartupShutdownBase, ISessionHandler<TSession>
        where TSession : IContentSession
        where TStore : IStartupShutdown
    {
        private const string Name = nameof(LocalContentServerBase<TStore, TSession>);
        private const string CheckForExpiredSessionsName = "CheckUnusedSessions";
        private const int CheckForExpiredSessionsPeriodMinutes = 1;

        private readonly IDisposable _portDisposer; // Null if port should not be exposed.
        private Server _grpcServer;

        private readonly ServiceReadinessChecker _serviceReadinessChecker;

        private readonly ConcurrentDictionary<int, SessionHandle<TSession>> _sessionHandles;
        private IntervalTimer _sessionExpirationCheckTimer;
        private IntervalTimer _logIncrementalStatsTimer;
        private Dictionary<string, long> _previousStatistics;

        private readonly Dictionary<string, AbsolutePath> _tempFolderForStreamsByCacheName = new Dictionary<string, AbsolutePath>();
        private readonly ConcurrentDictionary<int, DisposableDirectory> _tempDirectoryForStreamsBySessionId = new ConcurrentDictionary<int, DisposableDirectory>();

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

        /// <summary>
        /// Collection of stores by name.
        /// </summary>
        protected readonly Dictionary<string, TStore> StoresByName = new Dictionary<string, TStore>();

        /// <nodoc />
        protected LocalContentServerBase(
            ILogger logger,
            IAbsFileSystem fileSystem,
            string scenario,
            Func<AbsolutePath, TStore> contentStoreFactory,
            LocalServerConfiguration localContentServerConfiguration)
        {
            Contract.Requires(logger != null);
            Contract.Requires(fileSystem != null);
            Contract.Requires(localContentServerConfiguration != null);
            Contract.Requires(localContentServerConfiguration.GrpcPort > 0, "GrpcPort must be provided");

            logger.Debug($"{Name} process id {Process.GetCurrentProcess().Id}");
            logger.Debug($"{Name} constructing {nameof(ServiceConfiguration)}: {localContentServerConfiguration}");

            FileSystem = fileSystem;
            Logger = logger;
            Config = localContentServerConfiguration;

            _serviceReadinessChecker = new ServiceReadinessChecker(Tracer, logger, scenario);
            _sessionHandles = new ConcurrentDictionary<int, SessionHandle<TSession>>();

            foreach (var kvp in localContentServerConfiguration.NamedCacheRoots)
            {
                fileSystem.CreateDirectory(kvp.Value);
                var store = contentStoreFactory(kvp.Value);
                StoresByName.Add(kvp.Key, store);
            }

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
        protected abstract CreateSessionResult<TSession> CreateSession(TStore store, OperationContext context, string name, ImplicitPin implicitPin);

        private async Task<Result<long>> RemoveFromTrackerAsync(TStore store, OperationContext context, string storeName)
        {
            if (store is IRepairStore repairStore)
            {
                var result = await repairStore.RemoveFromTrackerAsync(context);
                if (!result)
                {
                    return Result.FromError<long>(result);
                }

                return Result.Success(result.Data);
            }

            Logger.Debug($"Repair handling not enabled for {storeName}'s content store.");
            return Result.Success(0L);
        }

        /// <inheritdoc />
        async Task<Result<CounterSet>> ISessionHandler<TSession>.GetStatsAsync(OperationContext context)
        {
            var counterSet = new CounterSet();
            foreach (var store in StoresByName.Values)
            {
                var stats = await GetStatsAsync(store, context);
                if (!stats)
                {
                    return Result.FromError<CounterSet>(stats);
                }

                counterSet.Merge(stats.CounterSet);
            }

            return counterSet;
        }

        /// <inheritdoc />
        async Task<Result<long>> ISessionHandler<TSession>.RemoveFromTrackerAsync(OperationContext context)
        {
            long filesEvicted = 0;
            foreach (var (name, store) in StoresByName)
            {
                var evictedResult = await RemoveFromTrackerAsync(store, context, name);
                if (!evictedResult)
                {
                    return evictedResult;
                }

                filesEvicted += evictedResult.Value;
            }

            return Result.Success(filesEvicted);
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
                context.TraceDebug($"{Name} creating temporary directory for session {sessionId}.");
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
            if (!FileSystem.DirectoryExists(Config.DataRootPath))
            {
                FileSystem.CreateDirectory(Config.DataRootPath);
            }

            await StartupStoresAsync(context).ThrowIfFailure();

            await LoadHibernatedSessionsAsync(context);

            InitializeAndStartGrpcServer(Config.GrpcPort, BindServices(), Config.RequestCallTokensPerCompletionQueue);

            _serviceReadinessChecker.Ready(context);

            _sessionExpirationCheckTimer = new IntervalTimer(
                () => CheckForExpiredSessionsAsync(context),
                MinTimeSpan(Config.UnusedSessionHeartbeatTimeout, TimeSpan.FromMinutes(CheckForExpiredSessionsPeriodMinutes)),
                message => Tracer.Debug(context, $"[{CheckForExpiredSessionsName}] message"));

            _logIncrementalStatsTimer = new IntervalTimer(
                () => LogIncrementalStatsAsync(context),
                Config.LogIncrementalStatsInterval);

            return BoolResult.Success;
        }

        private void InitializeAndStartGrpcServer(int grpcPort, ServerServiceDefinition[] definitions, int requestCallTokensPerCompletionQueue)
        {
            Contract.Requires(definitions.Length != 0);
            GrpcEnvironment.InitializeIfNeeded();
            _grpcServer = new Server
                          {
                              Ports = { new ServerPort(IPAddress.Any.ToString(), grpcPort, ServerCredentials.Insecure) },

                              // need a higher number here to avoid throttling: 7000 worked for initial experiments.
                              RequestCallTokensPerCompletionQueue = requestCallTokensPerCompletionQueue,
                          };

            foreach (var definition in definitions)
            {
                _grpcServer.Services.Add(definition);
            }

            _grpcServer.Start();
        }

        private async Task LogIncrementalStatsAsync(OperationContext context)
        {
            if (Interlocked.CompareExchange(ref _loggingIncrementalStats, 1, 0) != 0)
            {
                // Prevent re-entrancy so this method may be called during shutdown in addition
                // to being called in the timer
                return;
            }

            try
            {
                var statistics = new Dictionary<string, long>();
                var previousStatistics = _previousStatistics;
                foreach (var (name, store) in StoresByName)
                {
                    var stats = await GetStatsAsync(store, context);
                    if (stats.Succeeded)
                    {
                        var counters = stats.CounterSet.ToDictionaryIntegral();
                        FillTrackingStreamStatistics(counters);
                        foreach (var counter in counters)
                        {
                            var key = $"{name}.{counter.Key}";
                            var value = counter.Value;
                            statistics[key] = value;

                            if (previousStatistics != null && previousStatistics.TryGetValue(key, out var oldValue))
                            {
                                value -= oldValue;
                            }

                            Tracer.Info(context, $"IncrementalStatistic: {key}=[{value}]");
                        }
                    }
                }

                _previousStatistics = statistics;
            }
            finally
            {
                Volatile.Write(ref _loggingIncrementalStats, 0);
            }
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
                        Tracer.Debug(
                            context,
                            $"Releasing session {DescribeSession(sessionId, sessionHandle)}.");
                        await ReleaseSessionInternalAsync(context, sessionId);
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
            await TaskSafetyHelpers.WhenAll(tasks);

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

        private async Task LoadHibernatedSessionsAsync(OperationContext context)
        {
            try
            {
                if (FileSystem.HibernatedSessionsExists(Config.DataRootPath))
                {
                    try
                    {
                        var stopWatch = Stopwatch.StartNew();
                        var hibernatedSessions = await FileSystem.ReadHibernatedSessionsAsync(Config.DataRootPath);
                        stopWatch.Stop();
                        Tracer.Debug(
                            context, $"Read hibernated sessions from root=[{Config.DataRootPath}] in {stopWatch.Elapsed.TotalMilliseconds}ms");

                        if (hibernatedSessions.Sessions != null && hibernatedSessions.Sessions.Any())
                        {
                            foreach (HibernatedSessionInfo s in hibernatedSessions.Sessions)
                            {
                                Tracer.Debug(context, $"Restoring hibernated session {DescribeHibernatedSessionInfo(s)}.");

                                // If there was no expiration stored, then default to the longer timeout
                                // Otherwise, default to at least the shorter timeout
                                var newExpirationTicks = s.ExpirationUtcTicks == default(long)
                                    ? (DateTime.UtcNow + Config.UnusedSessionTimeout).Ticks
                                    : Math.Max(s.ExpirationUtcTicks, (DateTime.UtcNow + Config.UnusedSessionHeartbeatTimeout).Ticks);

                                var sessionResult = await CreateTempDirectoryAndSessionAsync(
                                    context,
                                    s.Id,
                                    s.Session,
                                    s.Cache,
                                    s.Pin,
                                    s.Capabilities,
                                    newExpirationTicks);

                                if (sessionResult.Succeeded)
                                {
                                    var session = sessionResult.Value.session;
                                    if (s.Pins != null && s.Pins.Any())
                                    {
                                        // Restore pins
                                        var contentHashes = s.Pins.Select(x => new ContentHash(x)).ToList();
                                        if (session is IHibernateContentSession hibernateSession)
                                        {
                                            await hibernateSession.PinBulkAsync(context, contentHashes);
                                        }
                                        else
                                        {
                                            foreach (var contentHash in contentHashes)
                                            {
                                                // Failure should be logged. We can ignore the error in this case.
                                                await session.PinAsync(context, contentHash, CancellationToken.None).IgnoreFailure();
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    Tracer.Warning(context, $"Failed to restore hibernated session, error=[{sessionResult.ErrorMessage}]");
                                }
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Tracer.Warning(context, $"Failed to read hibernated sessions root=[{Config.DataRootPath}]: {exception}");
                        Tracer.Error(context, exception);
                    }
                    finally
                    {
                        // Done reading hibernated sessions. No matter what happened, remove the file so we don't attempt to load later.
                        Tracer.Debug(context, $"Deleting hibernated sessions from root=[{Config.DataRootPath}].");
                        await FileSystem.DeleteHibernatedSessions(Config.DataRootPath);
                    }

                    _lastSessionId = _sessionHandles.Any() ? _sessionHandles.Keys.Max() : 0;
                }
            }
            catch (Exception exception)
            {
                Tracer.Error(context, exception, "Failed to load hibernated sessions");
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

            _logIncrementalStatsTimer?.Dispose();
            await LogIncrementalStatsAsync(context);

            // Stop the session expiration timer.
            _sessionExpirationCheckTimer?.Dispose();

            // Hibernate dangling sessions in case we are shutdown unexpectedly to live clients.
            await HandleShutdownDanglingSessionsAsync(context);

            // Cleaning up all the temp directories associated with sessions (they'll be recreated during the next startup when hibernated sessions are recreated).
            CleanSessionTempDirectories(context);

            // Now the stores, without active users, can be shut down.
            return await ShutdownStoresAsync(context);
        }

        private async Task HandleShutdownDanglingSessionsAsync(Context context)
        {
            try
            {
                if (_sessionHandles.Any())
                {
                    var sessionInfoList = new List<HibernatedSessionInfo>(_sessionHandles.Count);

                    foreach (var (sessionId, handle) in _sessionHandles)
                    {
                        TSession session = handle.Session;

                        if (session is IHibernateContentSession hibernateSession)
                        {
                            var pinnedContentHashes = hibernateSession.EnumeratePinnedContentHashes().Select(x => x.Serialize()).ToList();
                            Tracer.Debug(context, $"Hibernating session {DescribeSession(sessionId, handle)}.");
                            sessionInfoList.Add(new HibernatedSessionInfo(
                                sessionId,
                                handle.SessionName,
                                handle.ImplicitPin,
                                handle.CacheName,
                                pinnedContentHashes,
                                handle.SessionExpirationUtcTicks,
                                handle.SessionCapabilities));
                        }
                        else
                        {
                            Tracer.Warning(context, $"Shutdown of non-hibernating dangling session id={sessionId}");
                        }

                        await session.ShutdownAsync(context).ThrowIfFailure();
                        session.Dispose();
                    }

                    if (sessionInfoList.Any())
                    {
                        var hibernatedSessions = new HibernatedSessions(sessionInfoList);

                        try
                        {
                            var sw = Stopwatch.StartNew();
                            await hibernatedSessions.WriteAsync(FileSystem, Config.DataRootPath);
                            sw.Stop();
                            Tracer.Debug(
                                context, $"Wrote hibernated sessions to root=[{Config.DataRootPath}] in {sw.Elapsed.TotalMilliseconds}ms");
                        }
                        catch (Exception exception)
                        {
                            Tracer.Warning(context, $"Failed to write hibernated sessions root=[{Config.DataRootPath}]: {exception.ToString()}");
                            Tracer.Error(context, exception);
                        }
                    }

                    _sessionHandles.Clear();
                }
            }
            catch (Exception exception)
            {
                Tracer.Error(context, exception, "Failed to store hibernated sessions");
            }
        }

        private async Task<BoolResult> ShutdownStoresAsync(Context context)
        {
            var tasks = new List<Task<BoolResult>>(StoresByName.Count);
            tasks.AddRange(StoresByName.Select(kvp => kvp.Value.ShutdownAsync(context)));
            await TaskSafetyHelpers.WhenAll(tasks);

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

            _serviceReadinessChecker.Dispose();

            Logger.Debug($"{Name} disposed");
        }

        private void CleanSessionTempDirectories(OperationContext context)
        {
            int count = 0;
            context.TraceDebug($"{Name} cleaning up session's temp directories.");
            foreach (var tempDirectory in _tempDirectoryForStreamsBySessionId.Values)
            {
                count++;
                tempDirectory.Dispose();
            }

            _tempDirectoryForStreamsBySessionId.Clear();

            context.TraceDebug($"{Name} cleaned {count} session's temp directories.");
        }

        private Task<Result<TSession>> CreateSessionAsync(
            OperationContext context,
            string name,
            string cacheName,
            ImplicitPin implicitPin,
            int id,
            long sessionExpirationUtcTicks,
            Capabilities capabilities)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    TrySetBuildId(name);
                    if (!StoresByName.TryGetValue(cacheName, out var store))
                    {
                        return Result.FromErrorMessage<TSession>($"Cache by name=[{cacheName}] is not available");
                    }

                    var result = CreateSession(store, context, name, implicitPin).ThrowIfFailure();

                    var session = result.Session;
                    await session.StartupAsync(context).ThrowIfFailure();

                    var handle = new SessionHandle<TSession>(
                        session,
                        name,
                        cacheName,
                        implicitPin,
                        capabilities,
                        sessionExpirationUtcTicks,
                        GetTimeoutForCapabilities(capabilities));

                    bool added = _sessionHandles.TryAdd(id, handle);
                    if (!added)
                    {
                        // CreateSession should not be called for an id that is already presented in the internal map.
                        // The class members fully control the creation process, and the fact that the session is created is indication of a bug.
                        Contract.Assert(false, $"The session with id '{id}' is already created.");
                    }

                    Tracer.Debug(context, $"{nameof(CreateSessionAsync)} created session {handle.ToString(id)}.");
                    return Result.Success(session);
                });
        }

        private void TrySetBuildId(string sessionName)
        {
            // Domino provides build ID through session name for CB builds.
            if (Logger is IOperationLogger operationLogger && TryExtractBuildId(sessionName, out var buildId))
            {
                operationLogger.RegisterBuildId(buildId);
            }
        }

        private static bool TryExtractBuildId(string sessionName, out string buildId)
        {
            if (sessionName?.Contains(Context.BuildIdPrefix) == true)
            {
                var index = sessionName.IndexOf(Context.BuildIdPrefix) + Context.BuildIdPrefix.Length;
                buildId = sessionName.Substring(index);

                // Return true only if buildId is actually a guid.
                return Guid.TryParse(buildId, out _);
            }

            buildId = null;
            return false;
        }

        private void TryUnsetBuildId(string sessionName)
        {
            // Domino provides build ID through session name for CB builds.
            if (Logger is IOperationLogger operationLogger && TryExtractBuildId(sessionName, out _))
            {
                operationLogger.UnregisterBuildId();
            }
        }

        /// <summary>
        /// Try gets a session by session id.
        /// </summary>
        public TSession GetSession(int sessionId)
        {
            if (_sessionHandles.TryGetValue(sessionId, out var sessionHandle))
            {
                sessionHandle.BumpExpiration();
                return sessionHandle.Session;
            }

            return default;
        }

        private async Task<Result<(TSession session, int sessionId, AbsolutePath tempDirectory)>> CreateTempDirectoryAndSessionAsync(
            OperationContext context,
            int? sessionIdHint,
            string sessionName,
            string cacheName,
            ImplicitPin implicitPin,
            Capabilities capabilities,
            long sessionExpirationUtcTicks)
        {
            // The hint is provided when the session is recovered from hibernation.
            var sessionId = sessionIdHint ?? Interlocked.Increment(ref _lastSessionId);

            var tempDirectoryCreationResult = await CreateSessionTempDirectoryAsync(context, cacheName, sessionId);

            if (!tempDirectoryCreationResult)
            {
                return new Result<(TSession session, int sessionId, AbsolutePath tempDirectory)>(tempDirectoryCreationResult);
            }

            var sessionResult = await CreateSessionAsync(
                context,
                sessionName,
                cacheName,
                implicitPin,
                sessionId,
                sessionExpirationUtcTicks,
                capabilities);

            if (!sessionResult)
            {
                RemoveSessionTempDirectory(sessionId);
                return Result.FromError<(TSession session, int sessionId, AbsolutePath tempDirectory)>(sessionResult);
            }

            return Result.Success((sessionResult.Value, sessionId, tempDirectoryCreationResult.Value));
        }

        /// <inheritdoc />
        public async Task<Result<(int sessionId, AbsolutePath tempDirectory)>> CreateSessionAsync(
            OperationContext context,
            string sessionName,
            string cacheName,
            ImplicitPin implicitPin,
            Capabilities capabilities)
        {
            var result = await CreateTempDirectoryAndSessionAsync(
                context,
                sessionIdHint: null, // SessionId must be recreated for new sessions.
                sessionName,
                cacheName,
                implicitPin,
                capabilities,
                (DateTime.UtcNow + GetTimeoutForCapabilities(capabilities)).Ticks);

            if (!result)
            {
                return new Result<(int sessionId, AbsolutePath tempDirectory)>(result);
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

            string method = nameof(ISessionHandler<TSession>.ReleaseSessionAsync);
            if (sessionId < 0)
            {
                return;
            }

            if (!_sessionHandles.TryGetValue(sessionId, out var sessionHandle))
            {
                Tracer.Warning(context, $"{method} failed to lookup session id={sessionId}");
                return;
            }

            if (!_sessionHandles.TryRemove(sessionId, out _))
            {
                Tracer.Warning(context, $"{method} failed to remove entry for session id={sessionId}");
                return;
            }

            Tracer.Debug(context, $"{method} closing session {DescribeSession(sessionId, sessionHandle)}");

            TryUnsetBuildId(sessionHandle.SessionName);

            await sessionHandle.Session.ShutdownAsync(context).ThrowIfFailure();
            sessionHandle.Session.Dispose();
        }

        private string DescribeSession(int id, SessionHandle<TSession> handle) => handle.ToString(id);

        private string DescribeHibernatedSessionInfo(HibernatedSessionInfo info)
        {
            return $"id=[{info.Id}] name=[{info.Session}] expiration=[{info.ExpirationUtcTicks}] capabilities=[{info.Capabilities}] pins=[{info.Pins.Count}]";
        }
    }
}
