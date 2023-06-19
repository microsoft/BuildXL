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
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Utilities.Core.Tasks;
using ILogger = BuildXL.Cache.ContentStore.Interfaces.Logging.ILogger;

using GrpcEnvironment = BuildXL.Cache.ContentStore.Service.Grpc.GrpcEnvironment;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// A marker interface for a type that represents a real or a proxy content server.
    /// </summary>
    public interface ICacheServer : IStartupShutdown
    {
        /// <summary>
        /// Returns true if the current instance is not a real content server but rather a proxy for creating one
        /// in a separate process (like a generic launcher or an out-of-proc launcher).
        /// </summary>
        bool IsProxy { get; }

        /// <summary>
        /// Gets the default store used by the cache server.
        /// </summary>
        TStore GetDefaultStore<TStore>() where TStore : class, IStartupShutdown;

        /// <summary>
        /// An optional push file handler for handling incoming file requests.
        /// </summary>
        /// <remarks>
        /// Currently only used by the launcher.
        /// </remarks>
        IPushFileHandler? PushFileHandler { get; }

        /// <summary>
        /// A stream file store used for streaming the file content.
        /// </summary>
        /// <remarks>
        /// Currently only used by the launcher.
        /// </remarks>
        IDistributedStreamStore StreamStore { get; }

        /// <summary>
        /// Returns a list of gRPC.NET endpoints.
        /// </summary>
        IEnumerable<IGrpcServiceEndpoint> GrpcEndpoints { get; }
    }

    /// <summary>
    /// A special host for initializing and stopping gRPC.NET environment.
    /// </summary>
    public interface IContentServerGrpcHost
    {
        /// <summary>
        /// Notifies the host immediately before the cache service is started in order to start the gRPC infrastructure.
        /// </summary>
        Task<BoolResult> StartAsync(OperationContext context, LocalServerConfiguration configuration, ICacheServer cacheServer);

        /// <summary>
        /// Notifies the host immediately before cache service is stopped in order to stop the gRPC infrastructure.
        /// </summary>
        Task<BoolResult> StopAsync(OperationContext context, LocalServerConfiguration configuration);
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
    public abstract class LocalContentServerBase<TStore, TSession, TSessionData>
        : StartupShutdownBase, ISessionHandler<TSession, TSessionData>, ICacheServer
        where TSession : IContentSession
        where TStore : class, IStartupShutdown
        where TSessionData : ISessionData
    {
        private const string Name = nameof(LocalContentServerBase<TStore, TSession, TSessionData>);
        private const string CheckForExpiredSessionsName = "CheckUnusedSessions";
        private const int CheckForExpiredSessionsPeriodMinutes = 1;

        private readonly IDisposable? _portDisposer; // Null if port should not be exposed.
        private readonly IGrpcServiceEndpoint[] _additionalEndpoints;

        private readonly ServiceReadinessChecker _serviceReadinessChecker;

        private readonly ConcurrentDictionary<int, ISessionHandle<TSession, TSessionData>> _sessionHandles;
        private IntervalTimer? _sessionExpirationCheckTimer;

        private readonly Dictionary<string, AbsolutePath> _tempFolderForStreamsByCacheName = new(StringComparer.InvariantCultureIgnoreCase);
        private readonly ConcurrentDictionary<int, DisposableDirectory> _tempDirectoryForStreamsBySessionId = new();

        private int _lastSessionId;

        private readonly IGrpcServerHost<LocalServerConfiguration> _grpcHost;

        /// <nodoc />
        protected readonly LocalServerConfiguration Config;

        /// <nodoc />
        protected readonly IAbsFileSystem FileSystem;

        /// <nodoc />
        protected readonly ILogger Logger;

        /// <nodoc />
        protected abstract GrpcContentServer GrpcServer { get; }

        /// <nodoc />
        public IReadOnlyDictionary<string, TStore> StoresByName { get; }

        /// <inheritdoc />
        bool ICacheServer.IsProxy => false;

        /// <inheritdoc />
        // We have to use TStore2 to avoid having a conflict with TStore.
        // Technically, these two types could be different, but in practice they're the same.
        public TStore2 GetDefaultStore<TStore2>() where TStore2 : class, IStartupShutdown => (StoresByName["Default"] as TStore2)!;

        /// <inheritdoc />
        IPushFileHandler? ICacheServer.PushFileHandler => GrpcServer.PushFileHandler;

        /// <inheritdoc />
        IDistributedStreamStore ICacheServer.StreamStore => GrpcServer.StreamStore;

        /// <inheritdoc />
        public IEnumerable<IGrpcServiceEndpoint> GrpcEndpoints => new IGrpcServiceEndpoint[] { GrpcServer }.Concat(_additionalEndpoints);

        /// <nodoc />
        protected LocalContentServerBase(
            ILogger logger,
            IAbsFileSystem fileSystem,
            IGrpcServerHost<LocalServerConfiguration>? grpcHost,
            string scenario,
            Func<AbsolutePath, TStore> contentStoreFactory,
            LocalServerConfiguration configuration,
            IGrpcServiceEndpoint[]? additionalEndpoints)
        {
            Contract.Requires(logger != null);
            Contract.Requires(fileSystem != null);
            Contract.Requires(configuration != null);
            Contract.Requires(configuration.GrpcPort > 0, "GrpcPort must be provided");

            Contract.Requires(configuration.InitializeGrpcCoreServer || grpcHost != null, "grpcHost must be provided if 'InitializeGrpcCoreServer' is false.");
            _grpcHost = grpcHost ?? new GrpcCoreHost();

            logger.Debug($"{Name} process id {Process.GetCurrentProcess().Id}");
            logger.Debug($"{Name} constructing {nameof(ServiceConfiguration)}: {configuration}");

            if (configuration.InitializeGrpcCoreServer)
            {
                GrpcEnvironment.Initialize(logger, configuration.GrpcEnvironmentOptions, overwriteSafeOptions: true);
            }

            FileSystem = fileSystem;
            Logger = logger;
            Config = configuration;

            _additionalEndpoints = additionalEndpoints ?? Array.Empty<IGrpcServiceEndpoint>();
            _serviceReadinessChecker = new ServiceReadinessChecker(logger, scenario);
            _sessionHandles = new ConcurrentDictionary<int, ISessionHandle<TSession, TSessionData>>();

            var storesByName = new Dictionary<string, TStore>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var kvp in configuration.NamedCacheRoots)
            {
                fileSystem.CreateDirectory(kvp.Value);
                var store = contentStoreFactory(kvp.Value);
                storesByName.Add(kvp.Key, store);
            }

            StoresByName = new ReadOnlyDictionary<string, TStore>(storesByName);

            foreach (var kvp in configuration.NamedCacheRoots)
            {
                _tempFolderForStreamsByCacheName[kvp.Key] = kvp.Value / "TempFolder";
            }

            if (!string.IsNullOrEmpty(configuration.GrpcPortFileName))
            {
                var portSharingFactory = new MemoryMappedFileGrpcPortSharingFactory(logger, configuration.GrpcPortFileName);
                var portExposer = portSharingFactory.GetPortExposer();
                _portDisposer = portExposer.Expose(configuration.GrpcPort);
            }
        }

        /// <summary>
        /// Get list of current session IDs.
        /// </summary>
        public IReadOnlyList<int> GetSessionIds()
        {
            return _sessionHandles.Select(h => h.Key).ToList();
        }

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

            // The following method might fail with an exception and in that case we should not be calling post initialization completion.
            await tryStartupCoreAsync();

            foreach (var store in StoresByName.Values)
            {
                if (store is IContentStore contentStore)
                {
                    contentStore.PostInitializationCompleted(context);
                }
            }

            return BoolResult.Success;

            async Task tryStartupCoreAsync()
            {

                if (!FileSystem.DirectoryExists(Config.DataRootPath))
                {
                    FileSystem.CreateDirectory(Config.DataRootPath);
                }

                await StartupStoresAsync(context).ThrowIfFailure();

                foreach (var endpoint in GrpcEndpoints)
                {
                    await endpoint.StartupAsync(context).ThrowIfFailure();
                }

                await LoadHibernatedSessionsAsync(context);

                Tracer.Debug(context, $"Initializing gRPC Host at {Config.GrpcPort}. Id={InstanceId}");
                // The error is traced already, ignoring it.
                await _grpcHost.StartAsync(context, Config, GrpcEndpoints).IgnoreFailure();

                _serviceReadinessChecker.Ready(context);

                _sessionExpirationCheckTimer = new IntervalTimer(
                    () => CheckForExpiredSessionsAsync(context),
                    MinTimeSpan(Config.UnusedSessionHeartbeatTimeout, TimeSpan.FromMinutes(CheckForExpiredSessionsPeriodMinutes)),
                    logAction: message => Tracer.Debug(context, $"{CheckForExpiredSessionsName}: {message}"));
            }
        }

        /// <summary>
        /// Allows using encryption with the gRPC Core server
        /// </summary>
        private class GrpcCoreHost : GrpcCoreServerHost, IGrpcServerHost<LocalServerConfiguration>
        {
            protected override ServerCredentials? TryGetEncryptedCredentials(OperationContext context)
            {
                var encryptionOptions = GrpcEncryptionUtils.GetChannelEncryptionOptions();
                var keyCertPairResult = GrpcEncryptionUtils.TryGetSecureChannelCredentials(encryptionOptions.CertificateSubjectName, encryptionOptions.StoreLocation, out _);

                if (keyCertPairResult.Succeeded)
                {
                    Tracer.Debug(context, $"Found Grpc Encryption Certificate.");
                    return new SslServerCredentials(
                        new List<KeyCertificatePair> { new KeyCertificatePair(keyCertPairResult.Value.CertificateChain, keyCertPairResult.Value.PrivateKey) },
                        null,
                        SslClientCertificateRequestType.DontRequest); //Since this is an internal channel, client certificate is not requested or verified.
                }

                Tracer.Error(context, message: $"Failed to get GRPC SSL Credentials: {keyCertPairResult}");
                return null;
            }

            public Task<BoolResult> StartAsync(OperationContext context, LocalServerConfiguration configuration, IEnumerable<IGrpcServiceEndpoint> endpoints)
            {
                return StartAsync(context, Transform(configuration), endpoints);
            }

            public Task<BoolResult> StopAsync(OperationContext context, LocalServerConfiguration configuration)
            {
                return StopAsync(context, Transform(configuration));
            }

            private GrpcCoreServerHostConfiguration Transform(LocalServerConfiguration configuration)
            {
                return new GrpcCoreServerHostConfiguration(
                    configuration.GrpcPort,
                    configuration.EncryptedGrpcPort,
                    configuration.RequestCallTokensPerCompletionQueue,
                    configuration.GrpcCoreServerOptions);
            }
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

            Tracer.Debug(context, $"Stopping gRPC Host at {Config.GrpcPort}. Id={InstanceId}");
            await _grpcHost.StopAsync(context, Config).IgnoreFailure();

            var success = BoolResult.Success;

            foreach (var endpoint in GrpcEndpoints)
            {
                success &= await endpoint.ShutdownAsync(context);
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

            foreach (var store in StoresByName.Values)
            {
                store.Dispose();
            }

            _sessionExpirationCheckTimer?.Dispose();

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
                    Contract.Assert(added, $"The session with id '{id}' is already created.");
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
                requestAsyncShutdown().FireAndForget(context, traceErrorResult: true);
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
            if (this is TService typedService)
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
