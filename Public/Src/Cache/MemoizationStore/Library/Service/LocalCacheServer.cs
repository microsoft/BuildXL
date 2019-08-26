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
    public class LocalCacheServer : LocalContentServerBase<ICache, ICacheSession>
    {
        private readonly GrpcCacheServer _grpcCacheServer;
        private readonly GrpcContentServer _grpcContentServer;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(LocalCacheServer));

        /// <nodoc />
        public LocalCacheServer(
            IAbsFileSystem fileSystem,
            ILogger logger,
            string scenario,
            Func<AbsolutePath, ICache> cacheFactory,
            LocalServerConfiguration localContentServerConfiguration,
            Capabilities capabilities = Capabilities.All)
        : base(logger, fileSystem, scenario, cacheFactory, localContentServerConfiguration)
        {
            var storesByName = new Dictionary<string, IContentStore>();
            foreach (var kvp in localContentServerConfiguration.NamedCacheRoots)
            {
                fileSystem.CreateDirectory(kvp.Value);
                var cache = cacheFactory(kvp.Value);
                if (cache is IContentStore store)
                {
                    storesByName.Add(kvp.Key, store);
                }
                else
                {
                    throw new NotSupportedException($"Attempted to setup a cache that is not an IContentStore at path {kvp.Value}");
                }
            }

            _grpcContentServer = new GrpcContentServer(logger, capabilities, this, storesByName);
            _grpcCacheServer = new GrpcCacheServer(logger, this);
        }

        /// <inheritdoc />
        protected override ServerServiceDefinition[] BindServices() => new[] {_grpcContentServer.Bind(), _grpcCacheServer.Bind()};

        /// <inheritdoc />
        protected override Task<GetStatsResult> GetStatsAsync(ICache store, OperationContext context) => store.GetStatsAsync(context);

        /// <inheritdoc />
        protected override CreateSessionResult<ICacheSession> CreateSession(ICache store, OperationContext context, string name, ImplicitPin implicitPin) => store.CreateSession(context, name, implicitPin);

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _grpcContentServer.StartupAsync(context).ThrowIfFailure();

            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = await base.ShutdownCoreAsync(context);

            result &= await _grpcContentServer.ShutdownAsync(context);

            return result;
        }
    }
}
