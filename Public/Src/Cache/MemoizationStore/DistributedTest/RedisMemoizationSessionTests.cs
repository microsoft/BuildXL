// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using System;
using ContentStoreTest.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using Xunit;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    public class RedisMemoizationSessionTests : MemoizationSessionTests
    {
        private readonly MemoryClock _clock = new MemoryClock();
        private readonly LocalRedisFixture _redis;
        private readonly ILogger _logger;
        private readonly TimeSpan _memoizationExpiryTime = TimeSpan.FromDays(1);

        private readonly List<LocalRedisProcessDatabase> _databasesToDispose = new List<LocalRedisProcessDatabase>();

        public RedisMemoizationSessionTests(LocalRedisFixture redis, ITestOutputHelper helper)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, helper)
        {
            _redis = redis;
            _logger = TestGlobal.Logger;
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            var context = new Context(_logger);
            var keySpace = Guid.NewGuid().ToString();

            var primaryRedisInstance = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, _logger, _clock);
            _databasesToDispose.Add(primaryRedisInstance);

            var primaryFactory = RedisDatabaseFactory.CreateAsync(
                context,
                provider: new LiteralConnectionStringProvider(primaryRedisInstance.ConnectionString),
                logSeverity: Severity.Info,
                usePreventThreadTheft: false).GetAwaiter().GetResult();
            var primaryRedisAdapter = new RedisDatabaseAdapter(primaryFactory, keySpace: keySpace);

            var secondaryRedisInstance = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, _logger, _clock);
            _databasesToDispose.Add(secondaryRedisInstance);

            var secondaryFactory = RedisDatabaseFactory.CreateAsync(
                context,
                provider: new LiteralConnectionStringProvider(secondaryRedisInstance.ConnectionString),
                logSeverity: Severity.Info,
                usePreventThreadTheft: false).GetAwaiter().GetResult();
            var secondaryRedisAdapter = new RedisDatabaseAdapter(secondaryFactory, keySpace: keySpace);

            var memoizationDb = new RedisMemoizationDatabase(primaryRedisAdapter, secondaryRedisAdapter, _clock, _memoizationExpiryTime, operationsTimeout: null, slowOperationRedisTimeout: null);
            return new RedisMemoizationStore(_logger, memoizationDb);
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

    /// <summary>
    /// Custom collection that uses <see cref="LocalRedisFixture"/>.
    /// </summary>
    [CollectionDefinition("Redis-based tests")]
    public class LocalRedisCollection : ICollectionFixture<LocalRedisFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}
