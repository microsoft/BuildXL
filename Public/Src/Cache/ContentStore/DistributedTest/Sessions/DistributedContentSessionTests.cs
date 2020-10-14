// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Distributed.Redis;
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

        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            var rootPath = testDirectory.Path / "Root";
            var configurationModel = new ConfigurationModel(configuration);

            var primaryDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);
            var secondaryDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);

            var localMachineLocation = new MachineLocation(rootPath.Path);
            var storeFactory = new MockContentLocationStoreFactory(primaryDatabase, secondaryDatabase, rootPath);

            var settings = CreateSettings();

            return new DistributedContentStore(
                localMachineLocation,
                rootPath,
                (distributedStore) =>
                    new FileSystemContentStore(
                        FileSystem,
                        SystemClock.Instance,
                        rootPath,
                        configurationModel,
                        settings: ContentStoreSettings.DefaultSettings,
                        distributedStore: distributedStore),
                storeFactory,
                settings: settings,
                distributedCopier: storeFactory.GetCopier());
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
