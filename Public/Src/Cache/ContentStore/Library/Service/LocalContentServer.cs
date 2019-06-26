// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// IPC interface to a file system content cache.
    /// </summary>
    public class LocalContentServer : LocalContentServerBase<IContentStore, IContentSession>
    {
        private readonly GrpcContentServer _grpcContentServer;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(LocalContentServer));

        /// <nodoc />
        public LocalContentServer(
            IAbsFileSystem fileSystem,
            ILogger logger,
            string scenario,
            Func<AbsolutePath, IContentStore> contentStoreFactory,
            LocalServerConfiguration localContentServerConfiguration)
        : base(logger, fileSystem, scenario, contentStoreFactory, localContentServerConfiguration)
        {
            _grpcContentServer = new GrpcContentServer(logger, Capabilities.ContentOnly, this, StoresByName, localContentServerConfiguration);
        }

        /// <inheritdoc />
        protected override ServerServiceDefinition[] BindServices() => new[] { _grpcContentServer.Bind() };

        /// <inheritdoc />
        protected override Task<GetStatsResult> GetStatsAsync(IContentStore store, OperationContext context) => store.GetStatsAsync(context);

        /// <inheritdoc />
        protected override CreateSessionResult<IContentSession> CreateSession(IContentStore store, OperationContext context, string name, ImplicitPin implicitPin) => store.CreateSession(context, name, implicitPin);

        /// <summary>
        ///     Check if the service is running.
        /// </summary>
        public static bool EnsureRunning(Context context, string scenario, int waitMs) => ServiceReadinessChecker.EnsureRunning(context, scenario, waitMs);

        /// <summary>
        ///     Attempt to open event that will signal an imminent service shutdown or restart.
        /// </summary>
        public static EventWaitHandle OpenShutdownEvent(Context context, string scenario) => ServiceReadinessChecker.OpenShutdownEvent(context, scenario);
    }
}
