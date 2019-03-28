// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Extensions;
using ContentStoreTest.Sessions;
using ContentStoreTest.Stores;
using Xunit;

#pragma warning disable SA1402 // File may only contain a single class

namespace ContentStoreTest.Grpc
{
    public abstract class HeartbeatServiceClientContentSessionExpirationTests<T> : ServiceClientContentSessionTestBase<T>
        where T : ServiceClientContentStore, ITestServiceClientContentStore
    {
        private const int TimeoutSeconds = 3;
        private const int HeartbeatIntervalOverrideSeconds = 1;
        private static readonly LocalServerConfiguration ServerConfiguration = new LocalServerConfiguration()
        {
            UnusedSessionHeartbeatTimeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };

        protected HeartbeatServiceClientContentSessionExpirationTests(string scenario)
            : base(scenario)
        {
        }

        [Fact(Skip = "Session re-creation makes this obsolete")]
        public Task HeartbeatSessionExpiresQuicklyWhenNoHeartbeatSent()
        {
            // Set a heartbeat interval so long that it will time out before sending its first heartbeat
            const int longHeartbeatIntervalOverrideSeconds = TimeoutSeconds * 100;
            return RunStoreTestAsync(
                async (context, store) =>
                {
                    var sessionResult = store.CreateSession(context, CacheName, ImplicitPin.PutAndGet).ShouldBeSuccess();

                    using (var session = sessionResult.Session)
                    {
                        await session.StartupAsync(context).ShouldBeSuccess();

                        // Wait three periods to ensure that it will time out
                        await Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds * 3));

                        var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, MaxSize, Token).ShouldBeError();
                    }
                },
                ServerConfiguration,
                TimeSpan.FromSeconds(longHeartbeatIntervalOverrideSeconds));
        }

        [Fact]
        [Trait("Category", "QTestSkip")]
        public Task HeartbeatSessionExpiresQuicklyAfterDeath()
        {
            return RunStoreTestAsync(
                async (context, store) =>
                {
                    var sessionResult = store.CreateSession(context, CacheName, ImplicitPin.PutAndGet);
                    sessionResult.ShouldBeSuccess();

                    using (var session = sessionResult.Session)
                    {
                        await session.StartupAsync(context).ShouldBeSuccess();

                        var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, MaxSize, Token).ShouldBeSuccess();

                        await session.PinAsync(context, putResult.ContentHash, Token).ShouldBeSuccess();

                        // To simulate a crashing session, don't shut down this session
                    }

                    sessionResult = store.CreateSession(context, CacheName, ImplicitPin.PutAndGet).ShouldBeSuccess();

                    using (var session = sessionResult.Session)
                    {
                        var startupResult = await session.StartupAsync(context).ShouldBeSuccess();

                        var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, MaxSize, Token).ShouldBeError();

                        // Wait one period to ensure that it times out, another to ensure that the checker finds it, and another to give it time to release it.
                        await Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds * 3));

                        putResult = await session.PutRandomAsync(context, HashType.Vso0, false, MaxSize, Token).ShouldBeSuccess();
                    }
                },
                ServerConfiguration,
                TimeSpan.FromSeconds(HeartbeatIntervalOverrideSeconds));
        }

        [Fact]
        [Trait("Category", "QTestSkip")]
        public Task HeartbeatSessionDoesNotExpireAfterInactivity()
        {
            return RunStoreTestAsync(
                async (context, store) =>
                {
                    var sessionResult = store.CreateSession(context, CacheName, ImplicitPin.PutAndGet).ShouldBeSuccess();

                    using (var session = sessionResult.Session)
                    {
                        await session.StartupAsync(context).ShouldBeSuccess();

                        var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, MaxSize, Token).ShouldBeSuccess();

                        // Wait three periods to ensure that it would time out if not for the heartbeats
                        await Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds * 3));

                        await session.PinAsync(context, putResult.ContentHash, Token).ShouldBeSuccess();
                    }
                },
                ServerConfiguration,
                TimeSpan.FromSeconds(HeartbeatIntervalOverrideSeconds));
        }
    }

    [Trait("Category", "Integration")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class InProcessHeartbeatServiceClientContentSessionExpirationTests : HeartbeatServiceClientContentSessionExpirationTests<TestInProcessServiceClientContentStore>
    {
        public InProcessHeartbeatServiceClientContentSessionExpirationTests()
            : base(nameof(InProcessHeartbeatServiceClientContentSessionExpirationTests))
        {
        }

        protected InProcessHeartbeatServiceClientContentSessionExpirationTests(string scenario)
            : base(scenario)
        {
        }

        protected override TestInProcessServiceClientContentStore CreateStore(
            AbsolutePath rootPath,
            ContentStoreConfiguration configuration,
            LocalServerConfiguration localContentServerConfiguration,
            TimeSpan? heartbeatOverride)
        {
            configuration.Write(FileSystem, rootPath).Wait();

            var grpcPortFileName = Guid.NewGuid().ToString();
            var serviceConfiguration = new ServiceConfiguration(
                new Dictionary<string, AbsolutePath> { { CacheName, rootPath } },
                rootPath,
                MaxConnections,
                GracefulShutdownSeconds,
                PortExtensions.GetNextAvailablePort(),
                grpcPortFileName);

            return new TestInProcessServiceClientContentStore(
                FileSystem,
                Logger,
                CacheName,
                Scenario,
                heartbeatOverride,
                serviceConfiguration,
                localContentServerConfiguration: localContentServerConfiguration);
        }
    }

    // TODO: Failing locally during conversion
    [Trait("Category", "QTestSkip")]
    [Trait("Category", "Integration")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class ExternalProcessHeartbeatServiceClientContentSessionExpirationTests : HeartbeatServiceClientContentSessionExpirationTests<TestServiceClientContentStore>
    {
        public ExternalProcessHeartbeatServiceClientContentSessionExpirationTests()
            : base(nameof(ExternalProcessHeartbeatServiceClientContentSessionExpirationTests))
        {
        }

        protected ExternalProcessHeartbeatServiceClientContentSessionExpirationTests(string scenario)
            : base(scenario)
        {
        }

        protected override TestServiceClientContentStore CreateStore(
            AbsolutePath rootPath,
            ContentStoreConfiguration configuration,
            LocalServerConfiguration localContentServerConfiguration,
            TimeSpan? heartbeatOverride)
        {
            configuration.Write(FileSystem, rootPath).GetAwaiter().GetResult();

            var grpcPortFileName = Guid.NewGuid().ToString();
            var serviceConfiguration = new ServiceConfiguration(
                new Dictionary<string, AbsolutePath> { { CacheName, rootPath } },
                rootPath,
                MaxConnections,
                GracefulShutdownSeconds,
                PortExtensions.GetNextAvailablePort(),
                grpcPortFileName);

            return new TestServiceClientContentStore(
                Logger,
                FileSystem,
                CreateConfiguration(),
                heartbeatInterval: null,
                serviceConfiguration: serviceConfiguration,
                localContentServerConfiguration: localContentServerConfiguration);
        }
    }
}
