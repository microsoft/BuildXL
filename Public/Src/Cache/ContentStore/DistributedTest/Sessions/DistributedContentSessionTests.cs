// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using ContentStoreTest.Distributed.ContentLocation;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    [Collection("Redis-based tests")]
    [Trait("Category", "LongRunningTest")]
    public class DistributedContentSessionTests : ContentSessionTests
    {
        private readonly LocalRedisFixture _redis;

        internal static IReadOnlyList<TimeSpan> DefaultRetryIntervalsForTest = new List<TimeSpan>()
        {
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
        };

        public DistributedContentSessionTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, canHibernate: true, output)
        {
            _redis = redis;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            var rootPath = testDirectory.Path / "Root";
            var tempPath = testDirectory.Path / "Temp";
            var configurationModel = new ConfigurationModel(configuration);
            var fileCopier = new TestFileCopier();

            var localDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);
            var localMachineDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);

            var storeFactory = new MockRedisContentLocationStoreFactory(localDatabase, localMachineDatabase, rootPath);

            return new DistributedContentStore<AbsolutePath>(
                storeFactory.LocalMachineData,
                (nagleBlock, distributedEvictionSettings, contentStoreSettings, trimBulkAsync) =>
                    new FileSystemContentStore(
                        FileSystem,
                        SystemClock.Instance,
                        rootPath,
                        configurationModel,
                        nagleQueue: nagleBlock,
                        distributedEvictionSettings: distributedEvictionSettings,
                        settings: contentStoreSettings,
                        trimBulkAsync: trimBulkAsync),
                storeFactory,
                fileCopier,
                fileCopier,
                storeFactory.PathTransformer,
                copyRequester: null,
                ReadOnlyDistributedContentSession<AbsolutePath>.ContentAvailabilityGuarantee.FileRecordsExist,
                tempPath,
                FileSystem,
                RedisContentLocationStoreConstants.DefaultBatchSize,
                retryIntervalForCopies: DefaultRetryIntervalsForTest);
        }
    }
}
