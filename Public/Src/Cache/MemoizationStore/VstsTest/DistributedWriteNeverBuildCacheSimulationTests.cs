// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    [Trait("Category", "Simulation")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class DistributedWriteNeverBuildCacheSimulationTests : WriteNeverBuildCacheSimulationTests
    {
        public DistributedWriteNeverBuildCacheSimulationTests()
            : this(StorageOption.Item)
        {
        }

        protected DistributedWriteNeverBuildCacheSimulationTests(StorageOption storageOption)
            : base(storageOption)
        {
        }

        protected override ICache CreateCache(
            DisposableDirectory testDirectory,
            string cacheNamespace,
            IAbsFileSystem fileSystem,
            ILogger logger,
            BackingOption backingOption,
            StorageOption storageOption,
            TimeSpan? expiryMinimum = null,
            TimeSpan? expiryRange = null)
        {
            var innerCache = base.CreateCache(testDirectory, cacheNamespace, fileSystem, logger, backingOption, storageOption, expiryMinimum, expiryRange);
            return TestDistributedCacheFactory.CreateCache(
                logger, innerCache, cacheNamespace, nameof(DistributedWriteNeverBuildCacheSimulationTests), ReadThroughMode.None);
        }
    }
}
