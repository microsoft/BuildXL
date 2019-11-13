// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using Xunit;

namespace ContentStoreTest.Grpc
{
    public class GrpcRepairClientTests : TestBase
    {
        private const string CacheName = "CacheName";
        private const int WaitForServerReadyTimeoutMs = 10000;
        private const int WaitForExitTimeoutMs = 30000;
        private const LocalServerConfiguration LocalContentServerConfiguration = null;
        private const long DefaultMaxSize = 1 * 1024 * 1024;
        private const string Scenario = "RemoveFromTracker";

        public GrpcRepairClientTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        [Fact(Skip = "Start up fails - Bug #1245278")]
        public Task RepairHandlerGetsResponse()
        {
            return RunServerTestAsync(new Context(Logger), async (context, configuration) =>
            {
                var client = new GrpcRepairClient(configuration.GrpcPort);

                var removeFromTrackerResult = await client.RemoveFromTrackerAsync(context);
                removeFromTrackerResult.ShouldBeError();
                Assert.Equal("Repair handling not enabled.", removeFromTrackerResult.ErrorMessage);

                var sr = await client.ShutdownAsync(context);
                sr.ShouldBeSuccess();
            });
        }

        [Fact]
        public Task RepairClientThrowsDuringEvictWhenServiceDown()
        {
            return RunClientWhenServiceDownTestAsync(new Context(Logger), async (context, client) =>
            {
                await client.RemoveFromTrackerAsync(context).ShouldBeSuccess();
            });
        }

        private async Task RunServerTestAsync(Context context, Func<Context, ServiceConfiguration, Task> funcAsync)
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var storeConfig = CreateStoreConfiguration();
                await storeConfig.Write(FileSystem, directory.Path).ConfigureAwait(false);
                
                var serviceConfig = CreateServiceConfiguration(directory.Path, PortExtensions.GetNextAvailablePort(), Guid.NewGuid().ToString());

                using (var server = new ServiceProcess(serviceConfig, LocalContentServerConfiguration, Scenario, WaitForServerReadyTimeoutMs, WaitForExitTimeoutMs))
                {
                    BoolResult r = await server.StartupAsync(context).ConfigureAwait(false);
                    r.ShouldBeSuccess();

                    await funcAsync(context, serviceConfig);

                    r = await server.ShutdownAsync(context);
                    r.ShouldBeSuccess();
                }
            }
        }

        private async Task RunClientWhenServiceDownTestAsync(Context context, Func<Context, GrpcRepairClient, Task> funcAsync)
        {
            var exceptionThrown = false;

            using (var directory = new DisposableDirectory(FileSystem))
            {
                var configuration = CreateServiceConfiguration(directory.Path, PortExtensions.GetNextAvailablePort(), Guid.NewGuid().ToString());

                var client = new GrpcRepairClient(configuration.GrpcPort);

                try
                {
                    await funcAsync(context, client);
                }
                catch (ClientCanRetryException)
                {
                    exceptionThrown = true;
                }

                Assert.True(exceptionThrown);
            }
        }

        private ServiceConfiguration CreateServiceConfiguration(AbsolutePath path, int grpcPort, string grpcPortFileName)
        {
            return new ServiceConfiguration(
                new Dictionary<string, AbsolutePath> { { CacheName, path } },
                path,
                ServiceConfiguration.DefaultMaxConnections,
                ServiceConfiguration.DefaultGracefulShutdownSeconds,
                grpcPort,
                grpcPortFileName);
        }

        private ContentStoreConfiguration CreateStoreConfiguration()
        {
            return new ContentStoreConfiguration(new MaxSizeQuota($"{DefaultMaxSize}"));
        }
    }
}
