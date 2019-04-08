// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    [Collection("Redis-based tests")]
    [Trait("Category", "LongRunningTest")]
    public class RocksDbRedisContentLocationStoreTests : MemoryDbRedisContentLocationStoreTests, IDisposable
    {
        private readonly DisposableDirectory _workingDirectory;

        public RocksDbRedisContentLocationStoreTests(ITestOutputHelper output, LocalRedisFixture redis)
            : base(redis, output)
        {
            // Need to use unique folder for each test instance, because more then one test may be executed simultaneously.
            var uniqueOutputFolder = Guid.NewGuid().ToString();

            _workingDirectory = new DisposableDirectory(new PassThroughFileSystem(TestGlobal.Logger), Path.Combine(uniqueOutputFolder, "redis"));
            DefaultConfiguration = new RedisContentLocationStoreConfiguration()
                                   {
                                       Database = new RocksDbContentLocationDatabaseConfiguration(_workingDirectory.Path / "rocksdb")
                                   };

            output.WriteLine($"Tests output folder is '{_workingDirectory.Path}'");
        }

        protected override RedisContentLocationStoreConfiguration DefaultConfiguration { get; }

        /// <inheritdoc />
        public override void Dispose()
        {
            base.Dispose();
            _workingDirectory.Dispose();
        }

        /*
         * Case 1: Startup
         * - Acquire a role from the Redis Cluster state
         * - Get the latest checkpoint
         * - If the checkpoint exists:
         *     - Restore the RocksDb database
         *     - If master then get the offset and instantiate EventStore with a given offset
         * - if the checkpoint does not exist:
         *     - Instantiate a fresh instance of RocksDb database
         *     - If master then instantiate EventStore with no offset
         *
         * Case [Master]: Uploading the snapshot
         * (on timer)
         * - Get the current sequence point
         * - Create a checkpoint of the database
         * - Upload the checkpoint to the blob storage
         * - Update the Redis Cluster state with a newly uploaded blob + sequence point + potentially additional data
         *
         * Case [Worker]: Reload the snapshot
         * (on timer)
         * - Obtain the most recent snapshot
         * - Restore the RocksDb database
         *
         * Case 2: Master
         * Can Master restore the checkpoint?
         *
         * Case 2: Worker
         *
         * Case 2.1: Worker -> Master
         * - Send a heartbeat to Redis
         * - If the result is "Switch to Master" (this will be done in Lua, so no race condition here)
         *     - Get the latest checkpoint
         *         - If the checkpoint exists:
         *             - Restore the RocksDb database
         *             - "Reinitialize" an EventStore with a given offset
         *     - if the checkpoint does not exist:
         *         - Do nothing with RocksDb (will use the state that is there already)
         *         - "Reinstantiate" EventStore with no offset
         */
    }
}
