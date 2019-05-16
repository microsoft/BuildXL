// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Service;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    /// <summary>
    /// <see cref="ServiceClientCache"/> version that instantiates in-process cache instance and communicates to it via GRPC.
    /// </summary>
    public class TestInProcessServiceClientCache : StartupShutdownSlimBase, ICache
    {
        /// <summary>
        /// Server instance that this client communicates to.
        /// </summary>
        private readonly LocalCacheService _server;

        private readonly ServiceClientCache _client;

        /// <inheritdoc />
        protected override Tracer Tracer { get;} = new Tracer(nameof(TestInProcessServiceClientCache));

        /// <inheritdoc />
        public TestInProcessServiceClientCache(
            ILogger logger,
            IAbsFileSystem fileSystem,
            Func<AbsolutePath, ICache> contentStoreFactory,
            LocalServerConfiguration contentServerConfiguration,
            ServiceClientContentStoreConfiguration clientConfiguration)
        {
            // Initialize with fewer threads for tests
            GrpcEnvironment.InitializeIfNeeded(3);
            _server = new LocalCacheService(logger, fileSystem, clientConfiguration.Scenario, contentStoreFactory, contentServerConfiguration);
            _client = new ServiceClientCache(logger, fileSystem, clientConfiguration);
            SetThreadPoolSizes();
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _server.StartupAsync(context).ThrowIfFailure();
            await _client.StartupAsync(context).ThrowIfFailure();
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await _client.ShutdownAsync(context).ThrowIfFailure();

            if (!_server.ShutdownStarted)
            {
                await _server.ShutdownAsync(context).ThrowIfFailure();
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _client.Dispose();
            _server.Dispose();
        }

        /// <inheritdoc />
        public Guid Id => _client.Id;

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin) => _client.CreateReadOnlySession(context, name, implicitPin);

        /// <inheritdoc />
        public CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin) => _client.CreateSession(context, name, implicitPin);

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context) => _client.GetStatsAsync(context);

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context) => _client.EnumerateStrongFingerprints(context);

        private static void SetThreadPoolSizes()
        {
            ThreadPool.GetMaxThreads(out int workerThreads, out int completionPortThreads);
            workerThreads = Math.Max(workerThreads, Environment.ProcessorCount * 16);
            completionPortThreads = workerThreads;
            ThreadPool.SetMaxThreads(workerThreads, completionPortThreads);

            ThreadPool.GetMinThreads(out workerThreads, out completionPortThreads);
            workerThreads = Math.Max(workerThreads, Environment.ProcessorCount * 16);
            completionPortThreads = workerThreads;
            ThreadPool.SetMinThreads(workerThreads, completionPortThreads);
        }
    }
}
