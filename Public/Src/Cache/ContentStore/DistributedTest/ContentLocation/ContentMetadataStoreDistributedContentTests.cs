// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
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
        public Task TestPutAndRetrieveOnDifferentMachines()
        {
            var config = new MemoryContentMetadataStoreConfiguration(new RocksDbContentMetadataStore(
                TestClock,
                new RocksDbContentLocationDatabaseConfiguration(TestRootDirectoryPath / "rdbcms")
                {
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
                new Context(Logger),
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
        public Task TestPutAndRetrieveOnDifferentMachines2()
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
                new Context(Logger),
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
                }, ensureLiveness: true);
        }
    }
}
