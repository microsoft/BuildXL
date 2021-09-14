// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Service;
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// IPC interface to a file system content cache.
    /// </summary>
    public class LocalContentServer : LocalContentServerBase<IContentStore, IContentSession, LocalContentServerSessionData>
    {
        /// <summary>
        ///     Name of serialized data file.
        /// </summary>
        public const string HibernatedSessionsFileName = "sessions.json";

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(LocalContentServer));

        /// <inheritdoc />
        protected override ICacheServerServices Services => GrpcContentServer;

        internal GrpcContentServer GrpcContentServer { get; }

        /// <nodoc />
        public LocalContentServer(
            IAbsFileSystem fileSystem,
            ILogger logger,
            string scenario,
            Func<AbsolutePath, IContentStore> contentStoreFactory,
            LocalServerConfiguration localContentServerConfiguration,
            IGrpcServiceEndpoint[]? additionalEndpoints = null,
            IColdStorage? coldStorage = null)
        : base(logger, fileSystem, scenario, contentStoreFactory, localContentServerConfiguration, additionalEndpoints)
        {
            GrpcContentServer = new GrpcContentServer(logger, Capabilities.ContentOnly, this, StoresByName, localContentServerConfiguration, coldStorage);
        }

        /// <inheritdoc />
        protected override ServerServiceDefinition[] BindServices() => GrpcContentServer.Bind();

        /// <inheritdoc />
        protected override Task<GetStatsResult> GetStatsAsync(IContentStore store, OperationContext context) => store.GetStatsAsync(context);

        /// <inheritdoc />
        protected override CreateSessionResult<IContentSession> CreateSession(IContentStore store, OperationContext context, LocalContentServerSessionData sessionData) => store.CreateSession(context, sessionData.Name, sessionData.ImplicitPin);

        /// <summary>
        ///     Check if the service is running.
        /// </summary>
        public static bool EnsureRunning(Context context, string? scenario, int waitMs) => ServiceReadinessChecker.EnsureRunning(context, scenario, waitMs);

        /// <summary>
        ///     Attempt to open event that will signal an imminent service shutdown or restart.
        /// </summary>
        public static EventWaitHandle? OpenShutdownEvent(Context context, string? scenario) => ServiceReadinessChecker.OpenShutdownEvent(context, scenario);

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await GrpcContentServer.StartupAsync(context).ThrowIfFailure();

            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = await base.ShutdownCoreAsync(context);

            result &= await GrpcContentServer.ShutdownAsync(context);

            return result;
        }

        /// <inheritdoc />
        protected override Task<IReadOnlyList<HibernatedSessionInfo>> RestoreHibernatedSessionDatasAsync(OperationContext context)
            => RestoreHibernatedSessionDatasAsync(FileSystem, Config.DataRootPath);

        /// <nodoc />
        public static async Task<IReadOnlyList<HibernatedSessionInfo>> RestoreHibernatedSessionDatasAsync(
            IAbsFileSystem fileSystem,
            AbsolutePath rootPath)
        {
            var sessions = await fileSystem.ReadHibernatedSessionsAsync<HibernatedContentSessionInfo>(rootPath, HibernatedSessionsFileName);
            return sessions.Sessions.Select(si =>
                new HibernatedSessionInfo(
                    si.Id,
                    si.Cache,
                    si.ExpirationUtcTicks,
                    new LocalContentServerSessionData(si.Session, si.Capabilities, si.Pin, si.Pins),
                    si.Pins?.Count ?? 0)).ToList();
        }

        /// <inheritdoc />
        protected override Task RestoreHibernatedSessionStateAsync(OperationContext context, IContentSession session, LocalContentServerSessionData sessionData)
            => RestoreHibernatedSessionStateCoreAsync(context, session, sessionData);

        /// <nodoc />
        public static async Task RestoreHibernatedSessionStateCoreAsync(
            OperationContext context,
            IContentSession session,
            LocalContentServerSessionData sessionData)
        {
            if (sessionData.Pins != null && sessionData.Pins.Any())
            {
                // Restore pins
                var contentHashes = sessionData.Pins.Select(x => new ContentHash(x)).ToList();
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

        /// <inheritdoc />
        protected override Task HibernateSessionsAsync(Context context, IDictionary<int, ISessionHandle<IContentSession, LocalContentServerSessionData>> sessionHandles)
            => HibernateSessionsAsync(context, sessionHandles, Config, Tracer, FileSystem);

        /// <nodoc />
        public static async Task HibernateSessionsAsync(
            Context context,
            IDictionary<int, ISessionHandle<IContentSession, LocalContentServerSessionData>> sessionHandles,
            LocalServerConfiguration config,
            Tracer tracer,
            IAbsFileSystem fileSystem)
        {
            var sessionInfoList = new List<HibernatedContentSessionInfo>(sessionHandles.Count);

            foreach (var (sessionId, handle) in sessionHandles)
            {
                IContentSession session = handle.Session;

                if (session is IHibernateContentSession hibernateSession)
                {
                    if (config.ShutdownEvictionBeforeHibernation)
                    {
                        // Calling shutdown of eviction before hibernating sessions to prevent possible race condition of evicting pinned content
                        await hibernateSession.ShutdownEvictionAsync(context).ThrowIfFailure();
                    }

                    var pinnedContentHashes = hibernateSession.EnumeratePinnedContentHashes().Select(x => x.Serialize()).ToList();

                    tracer.Debug(context, $"Hibernating session {handle.ToString(sessionId)}.");
                    sessionInfoList.Add(new HibernatedContentSessionInfo(
                        sessionId,
                        handle.SessionData.Name,
                        handle.SessionData.ImplicitPin,
                        handle.CacheName,
                        pinnedContentHashes,
                        handle.SessionExpirationUtcTicks,
                        handle.SessionData.Capabilities));
                }
                else
                {
                    tracer.Warning(context, $"Shutdown of non-hibernating dangling session. {sessionId.AsTraceableSessionId()}");
                }

                await session.ShutdownAsync(context).ThrowIfFailure();
                session.Dispose();
            }

            if (sessionInfoList.Any())
            {
                var hibernatedSessions = new HibernatedSessions<HibernatedContentSessionInfo>(sessionInfoList);

                try
                {
                    var sw = Stopwatch.StartNew();
                    hibernatedSessions.Write(fileSystem, config.DataRootPath, HibernatedSessionsFileName);
                    sw.Stop();
                    tracer.Debug(
                        context, $"Wrote hibernated sessions to root=[{config.DataRootPath}] in {sw.Elapsed.TotalMilliseconds}ms");
                }
                catch (Exception exception)
                {
                    tracer.Error(context, exception, $"Failed to write hibernated sessions root=[{config.DataRootPath}]");
                }
            }
        }

        /// <inheritdoc />
        protected override bool HibernatedSessionsExist()
            => HibernatedSessionsExist(Config.DataRootPath, FileSystem);

        /// <nodoc />
        public static bool HibernatedSessionsExist(AbsolutePath rootPath, IAbsFileSystem fileSystem)
            => fileSystem.HibernatedSessionsExists(rootPath, HibernatedSessionsFileName);

        /// <inheritdoc />
        protected override Task CleanupHibernatedSessions()
            => CleanupHibernatedSessions(Config.DataRootPath, FileSystem);

        /// <nodoc />
        public static Task CleanupHibernatedSessions(AbsolutePath rootPath, IAbsFileSystem fileSystem)
            => fileSystem.DeleteHibernatedSessions(rootPath, HibernatedSessionsFileName);
    }
}
