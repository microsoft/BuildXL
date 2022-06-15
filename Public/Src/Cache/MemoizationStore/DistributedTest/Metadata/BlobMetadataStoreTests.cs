// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using ContentStoreTest.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Xunit;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit.Abstractions;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class BlobMetadataStoreTests : MemoizationSessionTests
    {
        private readonly MemoryClock _clock = new MemoryClock();
        private readonly LocalRedisFixture _redis;
        private readonly ILogger _logger;

        private readonly List<AzuriteStorageProcess> _databasesToDispose = new();

        public BlobMetadataStoreTests(LocalRedisFixture redis, ITestOutputHelper helper)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, helper)
        {
            _redis = redis;
            _logger = TestGlobal.Logger;
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            var instance = AzuriteStorageProcess.CreateAndStartEmpty(_redis, _logger);
            _databasesToDispose.Add(instance);

            var config = new BlobMetadataStoreConfiguration()
            {
                Credentials = new ContentStore.Interfaces.Secrets.AzureBlobStorageCredentials(instance.ConnectionString)
            };

            var store = new AzureBlobStorageMetadataStore(config);

            return new DatabaseMemoizationStore(new MetadataStoreMemoizationDatabase(store));
        }

        public override Task EnumerateStrongFingerprints(int strongFingerprintCount)
        {
            // Do nothing, since operation isn't supported in Redis.
            return Task.FromResult(0);
        }
        public override Task EnumerateStrongFingerprintsEmpty()
        {
            // Do nothing, since operation isn't supported in Redis.
            return Task.FromResult(0);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                foreach (var database in _databasesToDispose)
                {
                    database.Dispose();
                }
            }
        }
    }
}
