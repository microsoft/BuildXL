// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Sessions.Grpc;
using Grpc.Core;

namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <summary>
    /// IPC interface to a file system memoization store.
    /// </summary>
    public class LocalCacheService : LocalContentServerBase<ICache, ICacheSession>
    {
        private readonly GrpcCacheServer _grpcCacheServer;
        private readonly GrpcContentServer _grpcContentServer;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(LocalCacheService));

        /// <nodoc />
        public LocalCacheService(
            ILogger logger,
            IAbsFileSystem fileSystem,
            string scenario,
            Func<AbsolutePath, ICache> contentStoreFactory,
            LocalServerConfiguration localContentServerConfiguration,
            Capabilities capabilities = Capabilities.All)
        : base(logger, fileSystem, scenario, contentStoreFactory, localContentServerConfiguration)
        {
            var nameByDrive = new Dictionary<string, string>();

            foreach (var kvp in localContentServerConfiguration.NamedCacheRoots)
            {
                nameByDrive.Add(kvp.Value.DriveLetter.ToString(), kvp.Key);
            }

            // TODO: specify the right storeByName argument
            _grpcContentServer = new GrpcContentServer(logger, capabilities, this, nameByDrive, new Dictionary<string, IContentStore>());
            _grpcCacheServer = new GrpcCacheServer(logger, this);
        }

        /// <inheritdoc />
        protected override ServerServiceDefinition[] BindServices() => new[] {_grpcContentServer.Bind(), _grpcCacheServer.Bind()};

        /// <inheritdoc />
        protected override Task<GetStatsResult> GetStatsAsync(ICache store, OperationContext context) => store.GetStatsAsync(context);

        /// <inheritdoc />
        protected override CreateSessionResult<ICacheSession> CreateSession(ICache store, OperationContext context, string name, ImplicitPin implicitPin) => store.CreateSession(context, name, implicitPin);
    }
}
