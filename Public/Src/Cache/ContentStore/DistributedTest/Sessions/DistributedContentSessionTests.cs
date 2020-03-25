// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using BuildXL.Cache.ContentStore.Distributed;
using Test.BuildXL.TestUtilities.Xunit;

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
            var configurationModel = new ConfigurationModel(configuration);
            var fileCopier = new TestFileCopier();

            var localDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);
            var localMachineDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);

            var localMachineLocation = new MachineLocation(rootPath.Path);
            var storeFactory = new MockRedisContentLocationStoreFactory(localDatabase, localMachineDatabase, rootPath);

            var settings = CreateSettings();

            var distributedCopier = new DistributedContentCopier<AbsolutePath>(
                settings,
                FileSystem,
                fileCopier,
                fileCopier,
                copyRequester: null,
                storeFactory.PathTransformer,
                SystemClock.Instance);

            return new DistributedContentStore<AbsolutePath>(
                localMachineLocation,
                rootPath,
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
                settings: settings,
                distributedCopier: distributedCopier);
        }

        protected virtual DistributedContentStoreSettings CreateSettings()
        {
            return new DistributedContentStoreSettings
            {
                LocationStoreBatchSize = RedisContentLocationStoreConstants.DefaultBatchSize,
                RetryIntervalForCopies = DefaultRetryIntervalsForTest,
                SetPostInitializationCompletionAfterStartup = true
            };
        }
    }
}
