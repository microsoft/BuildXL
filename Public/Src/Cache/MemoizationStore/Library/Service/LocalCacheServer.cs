// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Sessions.Grpc;
using Grpc.Core;

namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <summary>
    /// IPC interface to a file system memoization store.
    /// </summary>
    public class LocalCacheServer : LocalContentServerBase<ICache, ICacheSession, LocalCacheServerSessionData>
    {
        /// <summary>
        ///     Name of serialized data file.
        /// </summary>
        public const string HibernatedSessionsFileName = "cache_sessions.json";

        private readonly GrpcCacheServer _grpcCacheServer;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(LocalCacheServer));

        /// <inheritdoc />
        protected override ICacheServerServices Services => _grpcCacheServer;

        /// <nodoc />
        public LocalCacheServer(
            IAbsFileSystem fileSystem,
            ILogger logger,
            string scenario,
            Func<AbsolutePath, ICache> cacheFactory,
            LocalServerConfiguration localContentServerConfiguration,
            Capabilities capabilities)
        : base(logger, fileSystem, scenario, cacheFactory, localContentServerConfiguration)
        {
            // This must agree with the base class' StoresByName to avoid "missing content store" errors from Grpc, and
            // to make sure everything is initialized properly when we expect it to.
            var storesByNameAsContentStore = StoresByName.ToDictionary(kvp => kvp.Key, kvp =>
            {
                var store = kvp.Value;
                if (store is IContentStore contentStore)
                {
                    return contentStore;
                }

                throw new ArgumentException(
                    $"Severe cache misconfiguration: {nameof(cacheFactory)} must generate instances that are " +
                    $"IContentStore. Instead, it generated {store.GetType()}.",
                    nameof(cacheFactory));
            });

            _grpcCacheServer = new GrpcCacheServer(logger, capabilities, this, storesByNameAsContentStore, localContentServerConfiguration);
        }

        /// <inheritdoc />
        protected override ServerServiceDefinition[] BindServices() => _grpcCacheServer.Bind();

        /// <inheritdoc />
        protected override Task<GetStatsResult> GetStatsAsync(ICache store, OperationContext context) => store.GetStatsAsync(context);

        /// <inheritdoc />
        protected override CreateSessionResult<ICacheSession> CreateSession(
            ICache store,
            OperationContext context,
            LocalCacheServerSessionData sessionData)
        {
            if (sessionData.PublishingConfig is not null)
            {
                if (store is not IPublishingCache publishingCache)
                {
                    return new CreateSessionResult<ICacheSession>("Specified a publishing configuration but the store is not publishing");
                }

                return publishingCache.CreatePublishingSession(context, sessionData.Name, sessionData.ImplicitPin, sessionData.PublishingConfig, sessionData.Pat);
            }

            return store.CreateSession(context, sessionData.Name, sessionData.ImplicitPin);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _grpcCacheServer.StartupAsync(context).ThrowIfFailure();

            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            // Tracing content server statistics at shutdown, because currently no one calls GetStats on this instance.
            Tracer.TraceStatisticsAtShutdown(context, _grpcCacheServer.Counters.ToCounterSet(), prefix: "GrpcContentServer");

            var result = await base.ShutdownCoreAsync(context);

            result &= await _grpcCacheServer.ShutdownAsync(context);

            return result;
        }

        /// <inheritdoc />
        protected override async Task<IReadOnlyList<HibernatedSessionInfo>> RestoreHibernatedSessionDatasAsync(OperationContext context)
        {
            var rootPath = Config.DataRootPath;

            var contentSessionDatas = await LocalContentServer.RestoreHibernatedSessionDatasAsync(FileSystem, rootPath);

            var infoDictionary = new Dictionary<int, HibernatedCacheSessionInfo>();

            if (FileSystem.HibernatedSessionsExists(rootPath, HibernatedSessionsFileName))
            {
                HibernatedSessions<HibernatedCacheSessionInfo> datas;
                try
                {
                    datas = await FileSystem.ReadProtectedHibernatedSessionsAsync<HibernatedCacheSessionInfo>(rootPath, HibernatedSessionsFileName);
                }
                catch (Exception e)
                {
                    Tracer.Debug(context, $"Failed to read protected hibernated cache sessions. Attempting to read unprotected data. Exception: {e}");
                    datas = await FileSystem.ReadHibernatedSessionsAsync<HibernatedCacheSessionInfo>(rootPath, HibernatedSessionsFileName);
                }

                foreach (var data in datas.Sessions)
                {
                    infoDictionary[data.Id] = data;
                }
            }

            return contentSessionDatas.Select(contentData =>
            {
                if (infoDictionary.ContainsKey(contentData.Id))
                {
                    var cacheData = infoDictionary[contentData.Id];
                    var (obj, type) = DynamicJson.Deserialize(cacheData.SerializedSessionConfiguration);
                    if (obj is not PublishingCacheConfiguration config)
                    {
                        throw new Exception($"Deserialized configuration is not an {nameof(PublishingCacheConfiguration)}. Actual type: {type}");
                    }

                    return new HibernatedSessionInfo(
                        contentData.Id,
                        contentData.CacheName,
                        contentData.ExpirationUtcTicks,
                        new LocalCacheServerSessionData(
                            contentData.SessionData.Name,
                            contentData.SessionData.Capabilities,
                            contentData.SessionData.ImplicitPin,
                            contentData.SessionData.Pins,
                            cacheData.Pat,
                            config,
                            cacheData.PendingPublishingOperations),
                        contentData.SessionData.Pins.Count);
                }
                else
                {
                    return new HibernatedSessionInfo(
                        contentData.Id,
                        contentData.CacheName,
                        contentData.ExpirationUtcTicks,
                        new LocalCacheServerSessionData(contentData.SessionData),
                        contentData.SessionData.Pins.Count);
                }
            }).ToList();
        }

        /// <inheritdoc />
        protected override async Task RestoreHibernatedSessionStateAsync(OperationContext context, ICacheSession session, LocalCacheServerSessionData sessionData)
        {
            await LocalContentServer.RestoreHibernatedSessionStateCoreAsync(context, session, sessionData);

            if (sessionData.PendingPublishingOperations != null && sessionData.PendingPublishingOperations.Any())
            {
                // Restore pending publishing operations.
                if (session is IHibernateCacheSession hibernateSession)
                {
                    await hibernateSession.SchedulePublishingOperationsAsync(context, sessionData.PendingPublishingOperations);
                }
                else
                {
                    Tracer.Warning(context, $"Failed to restore pending publishing operations for hibernated session. Session does not implement {nameof(IHibernateContentSession)}");
                }
            }
        }

        /// <inheritdoc />
        protected override async Task HibernateSessionsAsync(Context context, IDictionary<int, ISessionHandle<ICacheSession, LocalCacheServerSessionData>> sessionHandles)
        {
            var sessionInfoList = new List<HibernatedCacheSessionInfo>(sessionHandles.Count);
            foreach (var (id, handle) in sessionHandles)
            {
                if (handle.Session is IHibernateCacheSession hibernateSession)
                {
                    var pending = hibernateSession.GetPendingPublishingOperations();

                    var serializedConfig = DynamicJson.Serialize(handle.SessionData.PublishingConfig);

                    var info = new HibernatedCacheSessionInfo(
                        id,
                        serializedConfig,
                        handle.SessionData.Pat,
                        pending);

                    sessionInfoList.Add(info);
                }
            }
            var hibernatedSessions = new HibernatedSessions<HibernatedCacheSessionInfo>(sessionInfoList);

            try
            {
                await hibernatedSessions.WriteProtectedAsync(FileSystem, Config.DataRootPath, HibernatedSessionsFileName);
            }
            catch (Exception e) when (e is NotSupportedException || e is PlatformNotSupportedException)
            {
                Tracer.Debug(context, $"Failed to protect hibernated sessions because it is not supported by the current OS. " +
                    $"Attempting to hibernate while unprotected. Excepiton: {e}");
                await hibernatedSessions.WriteAsync(FileSystem, Config.DataRootPath, HibernatedSessionsFileName);
            }

            var contentHandles = new Dictionary<int, ISessionHandle<IContentSession, LocalContentServerSessionData>>(sessionHandles.Count);
            foreach (var (key, handle) in sessionHandles)
            {
                contentHandles[key] = handle;
            }
            await LocalContentServer.HibernateSessionsAsync(context, contentHandles, Config, Tracer, FileSystem);
        }

        /// <inheritdoc />
        protected override bool HibernatedSessionsExist() => LocalContentServer.HibernatedSessionsExist(Config.DataRootPath, FileSystem);

        /// <inheritdoc />
        protected override Task CleanupHibernatedSessions() => FileSystem.DeleteHibernatedSessions(Config.DataRootPath, HibernatedSessionsFileName);
    }
}
