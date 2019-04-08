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
using FluentAssertions;
using Xunit;

#pragma warning disable SA1402 // File may only contain a single class

namespace ContentStoreTest.Grpc
{
    public abstract class ServiceClientContentSessionExpirationTests<T> : ServiceClientContentSessionTestBase<T>
        where T : ServiceClientContentStore, ITestServiceClientContentStore
    {
        private const int TimeoutSeconds = 3;
        private static readonly LocalServerConfiguration ServerConfiguration = new LocalServerConfiguration()
        {
            UnusedSessionTimeout = TimeSpan.FromSeconds(TimeoutSeconds),
            UnusedSessionHeartbeatTimeout = TimeSpan.FromSeconds(1)
        };

        protected ServiceClientContentSessionExpirationTests(string scenario)
            : base(scenario)
        {
        }

        private static void DisableHeartbeat(IContentSession session)
        {
            var testSession = session as TestServiceClientContentSession;
            testSession.Should().NotBeNull();
            testSession?.StartupStarted.Should().BeFalse("Too late to disable heartbeat.");
            testSession?.DisableHeartbeat();
        }

        [Fact(Skip="Session re-creation makes this obsolete")]
        public Task SessionExpiresAfterInactivity()
        {
            return RunStoreTestAsync(
                async (context, store) =>
                {
                    var sessionResult = store.CreateSession(context, CacheName, ImplicitPin.PutAndGet);
                    sessionResult.ShouldBeSuccess();

                    using (var session = sessionResult.Session)
                    {
                        DisableHeartbeat(session);
                        await session.StartupAsync(context).ShouldBeSuccess();

                        var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, MaxSize, Token).ShouldBeSuccess();

                        // Wait one period to ensure that it times out, another to ensure that the checker finds it, and another to give it time to release it.
                        await Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds * 3));

                        var pinResult = await session.PinAsync(context, putResult.ContentHash, Token);
                        pinResult.Succeeded.Should().BeFalse();
                    }
                },
                ServerConfiguration);
        }

        [Fact]
        [Trait("Category", "QTestSkip")]
        public Task SessionExpiresAfterDeath()
        {
            return RunStoreTestAsync(
                async (context, store) =>
                {
                    var sessionResult = store.CreateSession(context, CacheName, ImplicitPin.PutAndGet).ShouldBeSuccess();

                    using (var session = sessionResult.Session)
                    {
                        DisableHeartbeat(session);
                        await session.StartupAsync(context).ShouldBeSuccess();

                        var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, MaxSize, Token).ShouldBeSuccess();

                        await session.PinAsync(context, putResult.ContentHash, Token).ShouldBeSuccess();

                        // To simulate a crashing session, don't shut down this session
                    }

                    sessionResult = store.CreateSession(context, CacheName, ImplicitPin.PutAndGet).ShouldBeSuccess();

                    // Wait one period to ensure that it times out, another to ensure that the checker finds it, and another to give it time to release it.
                    await Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds * 3));

                    using (var session = sessionResult.Session)
                    {
                        DisableHeartbeat(session);
                        await session.StartupAsync(context).ShouldBeSuccess();

                        await session.PutRandomAsync(context, HashType.Vso0, false, MaxSize, Token).ShouldBeSuccess();
                    }
                },
                ServerConfiguration);
        }

        [Fact]
        [Trait("Category", "QTestSkip")]
        public Task SessionDoesNotExpireGivenConstantActivity()
        {
            return RunStoreTestAsync(
                async (context, store) =>
                {
                    var sessionResult = store.CreateSession(context, CacheName, ImplicitPin.PutAndGet);
                    sessionResult.ShouldBeSuccess();

                    using (var session = sessionResult.Session)
                    {
                        DisableHeartbeat(session);
                        var startupResult = await session.StartupAsync(context);
                        startupResult.ShouldBeSuccess();

                        var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, MaxSize, Token);
                        putResult.ShouldBeSuccess();

                        // Wait three periods to ensure that it would time out if not for the activity
                        for (int i = 0; i < TimeoutSeconds * 3; i++)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            var pinResult = await session.PinAsync(context, putResult.ContentHash, Token);
                            pinResult.ShouldBeSuccess();
                        }
                    }
                },
                ServerConfiguration);
        }
    }

    [Trait("Category", "Integration")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class InProcessServiceClientContentSessionExpirationTests : ServiceClientContentSessionExpirationTests<TestInProcessServiceClientContentStore>
    {
        public InProcessServiceClientContentSessionExpirationTests()
            : base(nameof(InProcessServiceClientContentSessionExpirationTests))
        {
        }

        protected InProcessServiceClientContentSessionExpirationTests(string scenario)
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

            return new TestInProcessServiceClientContentStore
            (
                FileSystem,
                Logger,
                CacheName,
                Scenario,
                heartbeatOverride,
                serviceConfiguration,
                localContentServerConfiguration: localContentServerConfiguration
            );
        }
    }

    // TODO: Failing locally during conversion
    [Trait("Category", "QTestSkip")]
    [Trait("Category", "Integration")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class ExternalProcessServiceClientContentSessionExpirationTests : ServiceClientContentSessionExpirationTests<TestServiceClientContentStore>
    {
        public ExternalProcessServiceClientContentSessionExpirationTests()
            : base(nameof(ExternalProcessServiceClientContentSessionExpirationTests))
        {
        }

        protected ExternalProcessServiceClientContentSessionExpirationTests(string scenario)
            : base(scenario)
        {
        }

        protected override TestServiceClientContentStore CreateStore(
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

            return new TestServiceClientContentStore(
                Logger,
                FileSystem,
                new ServiceClientContentStoreConfiguration(CacheName, null, Scenario), 
                heartbeatOverride,
                serviceConfiguration,
                localContentServerConfiguration: localContentServerConfiguration
            );
        }
    }
}
