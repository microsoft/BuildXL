// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using ContentStoreTest.Extensions;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    /// <summary>
    /// Set of tests for <see cref="ICache"/> implementation that communicates to an actual cache over GRPC.
    /// </summary>
    public sealed class InProcessServiceLocalCacheTests : LocalCacheTests
    {
        /// <inheritdoc />
        public InProcessServiceLocalCacheTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private const string CacheName = "CacheName";

        /// <inheritdoc />
        protected override ICache CreateCache(DisposableDirectory testDirectory)
        {
            var backendCacheDirectory = testDirectory.Path / "Backend";
            FileSystem.CreateDirectory(backendCacheDirectory);

            var namedCacheRoots = new Dictionary<string, AbsolutePath> {[CacheName] = backendCacheDirectory / "Root"};

            var grpcPort = PortExtensions.GetNextAvailablePort();
            var serverConfiguration = new LocalServerConfiguration(backendCacheDirectory, namedCacheRoots, grpcPort)
            {
                GrpcPortFileName = null, // Port is well known at configuration time, no need to expose it.
            };
            var serviceClientConfiguration = new ServiceClientContentStoreConfiguration(CacheName, ServiceClientRpcConfiguration.CreateGrpc(serverConfiguration.GrpcPort), "Scenario-" + Guid.NewGuid());
            Func<AbsolutePath, ICache> contentStoreFactory = CreateBackendCache;
            var serviceClient = new TestInProcessServiceClientCache(Logger, FileSystem, contentStoreFactory, serverConfiguration, serviceClientConfiguration);
            return serviceClient;
        }

        private ICache CreateBackendCache(AbsolutePath rootPath)
        {
            var configuration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
            var configurationModel = new ConfigurationModel(configuration);

            return new OneLevelCache(
                () => new FileSystemContentStore(FileSystem, SystemClock.Instance, rootPath, configurationModel),
                () => new MemoryMemoizationStore(Logger),
                CacheDeterminism.NewCacheGuid());
        }

        /// <inheritdoc />
        public override Task ConcurrentCaches() => BoolResult.SuccessTask;

        /// <inheritdoc />
        public override Task IdIsPersistent() => BoolResult.SuccessTask;

        /// <inheritdoc />
        public override Task EnumerateStrongFingerprints(int strongFingerprintCount) => BoolResult.SuccessTask;

        protected override Task ContentStoreStartupFails() => BoolResult.SuccessTask;
        
        protected override Task MemoizationStoreStartupFails() => BoolResult.SuccessTask;

        protected override Task VerifyPinCallCounterBumpedOnUse(ICache cache, Context context) => BoolResult.SuccessTask;

        protected override Task VerifyOpenStreamCallCounterBumpedOnUse(ICache cache, Context context) => BoolResult.SuccessTask;

        protected override Task VerifyPlaceFileCallCounterBumpedOnUse(ICache cache, Context context) => BoolResult.SuccessTask;

        protected override Task VerifyPutFileCallCounterBumpedOnUse(ICache cache, Context context) => BoolResult.SuccessTask;

        protected override Task VerifyPutStreamCallCounterBumpedOnUse(ICache cache, Context context) => BoolResult.SuccessTask;
    }
}
