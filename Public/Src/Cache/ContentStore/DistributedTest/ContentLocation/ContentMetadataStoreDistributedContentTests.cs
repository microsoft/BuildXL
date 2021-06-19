// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.Host.Configuration;
using ContentStoreTest.Distributed.Redis;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    [Trait("Category", "Integration")]
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    public partial class ContentMetadataStoreDistributedContentTests : LocalLocationStoreDistributedContentTestsBase
    {
        public ContentMetadataStoreDistributedContentTests(
            LocalRedisFixture redis,
            ITestOutputHelper output)
            : base(redis, output)
        {
        }

        [Fact]
        public Task TestPutAndRetrieveOnDifferentMachinesNoRpc()
        {
            var config = new MemoryContentMetadataStoreConfiguration(new RocksDbContentMetadataStore(
                TestClock,
                new RocksDbContentMetadataStoreConfiguration() {
                    Database = new RocksDbContentLocationDatabaseConfiguration(TestRootDirectoryPath / "rdbcms")
                    {
                    },
                }));

            ConfigureWithOneMaster(
                overrideDistributed: d =>
                {
                    d.ContentMetadataStoreMode = ContentMetadataStoreMode.Distributed;
                },
                overrideRedis: r =>
                {
                    r.MetadataStore = config;
                });

            return RunTestAsync(
                3,
                async context =>
                {
                    var sessions = context.Sessions;
                    var master = context.GetMasterIndex();
                    var worker0 = context.EnumerateWorkersIndices().ElementAt(0);
                    var worker1 = context.EnumerateWorkersIndices().ElementAt(1);

                    var workerSession0 = sessions[worker0];
                    var workerSession1 = sessions[worker1];

                    var putResult = await workerSession0.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    await OpenStreamAndDisposeAsync(workerSession1, context, putResult.ContentHash);
                });
        }

        [Fact]
        public Task TestPutAndRetrieveOnDifferentMachines()
        {
            UseGrpcServer = true;

            ConfigureWithOneMaster(
                overrideDistributed: d =>
                {
                    d.ContentMetadataStoreMode = d.TestMachineIndex switch
                    {
                        0 => ContentMetadataStoreMode.Redis,
                        _ => ContentMetadataStoreMode.WriteBothPreferRedis
                    };
                });

            return RunTestAsync(
                3,
                async context =>
                {
                    var sessions = context.Sessions;
                    var contexts = context.StoreContexts;
                    var master = context.GetMasterIndex();
                    var worker0 = context.EnumerateWorkersIndices().ElementAt(0);
                    var worker1 = context.EnumerateWorkersIndices().ElementAt(1);

                    var workerSession0 = sessions[worker0];
                    var workerSession1 = sessions[worker1];

                    var putResult = await workerSession0.PutRandomAsync(contexts[worker0], ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    await OpenStreamAndDisposeAsync(workerSession1, contexts[worker1], putResult.ContentHash);
                },
                ensureLiveness: true);
        }

        [Fact]
        public Task TestServicePutAndRetrieveOnDifferentMachines()
        {
            UseGrpcServer = true;

            ConfigureWithOneMaster(
                overrideDistributed: d =>
                {
                    d.ContentMetadataEnableResilience = true;
                    d.ContentMetadataStoreMode = ContentMetadataStoreMode.Distributed;
                    d.ContentMetadataPersistInterval = "1000s";
                },
                overrideRedis: r =>
                {
                    //r.MetadataStore = config;
                });

            return RunTestAsync(
                3,
                async context =>
                {
                    var sessions = context.Sessions;
                    var master = context.GetMasterIndex();
                    var worker0 = context.EnumerateWorkersIndices().ElementAt(0);
                    var worker1 = context.EnumerateWorkersIndices().ElementAt(1);

                    var masterStore = context.GetLocalLocationStore(master);
                    //masterStore.HeartbeatAsync(context, )

                    var workerSession0 = sessions[worker0];
                    var workerSession1 = sessions[worker1];

                    var putResult = await workerSession0.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    await OpenStreamAndDisposeAsync(workerSession1, context, putResult.ContentHash);
                });
        }

        [Fact]
        public Task TestServicePutAndRetrieveOnDifferentMachinesWithRecovery()
        {
            _overrideScenarioName = Guid.NewGuid().ToString();
            UseGrpcServer = true;

            ConfigureWithOneMaster(
                overrideDistributed: d =>
                {
                    d.ContentMetadataEnableResilience = true;
                    d.ContentMetadataStoreMode = ContentMetadataStoreMode.Distributed;
                    d.ContentMetadataPersistInterval = "1000s";
                    d.CreateCheckpointIntervalMinutes = 10;
                });

            PutResult putResult = null;

            return RunTestAsync(
                3,
                async context =>
                {
                    var sessions = context.Sessions;
                    var master = context.GetMasterIndex();
                    var worker0 = context.EnumerateWorkersIndices().ElementAt(0);
                    var worker1 = context.EnumerateWorkersIndices().ElementAt(1);

                    var masterStore = context.GetLocalLocationStore(master);

                    var workerSession0 = sessions[worker0];
                    var workerSession1 = sessions[worker1];

                    if (context.Iteration == 0)
                    {
                        putResult = await workerSession0.PutRandomAsync(context.StoreContexts[worker0], ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                    }
                    else if (context.Iteration == 1)
                    {
                        await OpenStreamAndDisposeAsync(workerSession1, context.StoreContexts[worker1], putResult.ContentHash);
                    }
                },
                iterations: 2);
        }

        [Fact]
        public Task TestServicePutAndRetrieveOnDifferentMachinesWithMasterSwitch()
        {
            _overrideScenarioName = Guid.NewGuid().ToString();
            UseGrpcServer = true;

            ConfigureWithOneMaster(
                overrideDistributed: d =>
                {
                    // On first iteration machine 0 is master.
                    // On second iteration, both machines may be master machines
                    d.IsMasterEligible = d.TestIteration >= d.TestMachineIndex;
                    d.ContentMetadataEnableResilience = true;
                    d.ContentMetadataStoreMode = ContentMetadataStoreMode.Distributed;
                    d.ContentMetadataPersistInterval = "1000s";
                    d.CreateCheckpointIntervalMinutes = 10;
                });

            PutResult putResult = null;

            return RunTestAsync(
                3,
                async context =>
                {
                    if (context.Iteration == 1)
                    {
                        var lls0 = context.GetLocalLocationStore(0);
                        var cms0 = context.GetContentMetadataService(0);
                        var lls1 = context.GetLocalLocationStore(1);
                        var cms1 = context.GetContentMetadataService(1);

                        await lls0.CreateCheckpointAsync(context.StoreContexts[0]).ShouldBeSuccess();

                        // Disable the service
                        await cms0.OnSuccessfulHeartbeatAsync(context, Role.Worker);

                        // Heartbeat to start restore checkpoint
                        var restoreTask = lls1.HeartbeatAsync(context.StoreContexts[1], forceRestore: true);

                        // Increase time to allow master expiry
                        TestClock.UtcNow += TimeSpan.FromHours(1);

                        // Heartbeat to change role
                        await lls1.HeartbeatAsync(context.StoreContexts[1]).ShouldBeSuccess();

                        await restoreTask.ShouldBeSuccess();

                        for (int i = 0; i < context.Stores.Count; i++)
                        {
                            await context.GetLocalLocationStore(i).HeartbeatAsync(context).ShouldBeSuccess();
                        }
                    }

                    var sessions = context.Sessions;
                    var master = context.GetMasterIndex();
                    var worker0 = context.EnumerateWorkersIndices().ElementAt(0);
                    var worker1 = context.EnumerateWorkersIndices().ElementAt(1);

                    var masterStore = context.GetLocalLocationStore(master);

                    var workerSession0 = sessions[worker0];
                    var workerSession1 = sessions[worker1];

                    if (context.Iteration == 0)
                    {
                        putResult = await workerSession0.PutRandomAsync(context.StoreContexts[worker0], ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                    }
                    else if (context.Iteration == 1)
                    {
                        await OpenStreamAndDisposeAsync(workerSession1, context.StoreContexts[worker1], putResult.ContentHash);
                    }
                },
                iterations: 2);
        }

        [Fact]
        public Task TestServicePutAndRetrieveOnDifferentMachinesWithCheckpoint()
        {
            _overrideScenarioName = Guid.NewGuid().ToString();
            UseGrpcServer = true;

            ConfigureWithOneMaster(
                overrideDistributed: d =>
                {
                    d.ContentMetadataEnableResilience = true;
                    d.ContentMetadataStoreMode = ContentMetadataStoreMode.Distributed;
                    d.ContentMetadataPersistInterval = "1000s";
                    d.CreateCheckpointIntervalMinutes = 10;
                });

            PutResult putResult = null;

            return RunTestAsync(
                3,
                async context =>
                {
                    var sessions = context.Sessions;
                    var master = context.GetMasterIndex();
                    var worker0 = context.EnumerateWorkersIndices().ElementAt(0);
                    var worker1 = context.EnumerateWorkersIndices().ElementAt(1);

                    var masterStore = context.GetLocalLocationStore(master);

                    var workerSession0 = sessions[worker0];
                    var workerSession1 = sessions[worker1];

                    if (context.Iteration == 0)
                    {
                        putResult = await workerSession0.PutRandomAsync(context.StoreContexts[worker0], ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                        await context.GetContentMetadataService().CreateCheckpointAsync(context).ShouldBeSuccess();
                    }
                    else if (context.Iteration == 1)
                    {
                        await OpenStreamAndDisposeAsync(workerSession1, context.StoreContexts[worker1], putResult.ContentHash);
                    }
                },
                iterations: 2);
        }
    }
}
