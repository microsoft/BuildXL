// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service.Grpc;
using ContentStoreTest.Stores;
using ContentStoreTest.Test;
using Xunit.Abstractions;

namespace ContentStoreTest.Sessions
{
    public abstract class ServiceClientContentSessionTestBase<T> : TestBase
        where T : ServiceClientContentStore, ITestServiceClientContentStore
    {
        protected ServiceClientContentStoreConfiguration CreateConfiguration()
        {
            return new ServiceClientContentStoreConfiguration(CacheName, rpcConfiguration: null, scenario: Scenario);
        }

        protected const string CacheName = "test";
        protected const uint MaxConnections = 4;
        protected const uint ConnectionsPerSession = 2;
        protected const uint GracefulShutdownSeconds = ServiceConfiguration.DefaultGracefulShutdownSeconds;

        protected virtual string SessionName { get; set; } = "name";

        protected const int ContentByteCount = 100;
        protected const HashType ContentHashType = HashType.Vso0;
        private const long DefaultMaxSize = 1 * 1024 * 1024;
        protected static readonly CancellationToken Token = CancellationToken.None;
        protected readonly string Scenario;

        protected ServiceClientContentSessionTestBase(string scenario, ITestOutputHelper output = null)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
            // Reducing the number of threads for tests for performance reasons.
            GrpcEnvironment.InitializeIfNeeded(3);
            Scenario = scenario + ScenarioSuffix;
        }

        protected static long MaxSize => DefaultMaxSize;

        protected async Task RunSessionTestAsync(
            Context context, IContentStore store, ImplicitPin implicitPin, Func<Context, IContentSession, Task> funcAsync)
        {
            var createResult = store.CreateSession(context, SessionName, implicitPin);
            createResult.ShouldBeSuccess();
            using (var session = createResult.Session)
            {
                try
                {
                    await session.StartupAsync(context).ShouldBeSuccess();
                    await funcAsync(context, session);
                }
                finally
                {
                    await session.ShutdownAsync(context).ShouldBeSuccess();
                }
            }
        }

        protected async Task RunStoreTestAsync(Func<Context, IContentStore, Task> funcAsync, LocalServerConfiguration localContentServerConfiguration = null, TimeSpan? heartbeatOverride = null)
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var config = new ContentStoreConfiguration(new MaxSizeQuota($"{DefaultMaxSize}"));

                using (var store = CreateStore(directory.Path, config, localContentServerConfiguration ?? TestConfigurationHelper.LocalContentServerConfiguration, heartbeatOverride))
                {
                    try
                    {
                        await store.StartupAsync(context).ShouldBeSuccess();
                        await funcAsync(context, store);
                    }
                    finally
                    {
                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
            }
        }

        protected Task RunSessionTestAsync(
            ImplicitPin implicitPin,
            Func<Context, IContentSession, Task> funcAsync,
            LocalServerConfiguration localContentServerConfiguration = null)
        {
            return RunStoreTestAsync(
                (context, store) => RunSessionTestAsync(context, store, implicitPin, funcAsync),
                localContentServerConfiguration);
        }

        protected abstract T CreateStore(AbsolutePath rootPath, ContentStoreConfiguration configuration, LocalServerConfiguration localContentServerConfiguration, TimeSpan? heartbeatOverride);
    }
}
