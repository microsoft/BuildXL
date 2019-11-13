// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Stores;
using ContentStoreTest.Synchronization;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Test.Synchronization
{
    // ReSharper disable once UnusedMember.Global
    [Trait("Category", "WindowsOSOnly")] // Likely failing in OSX due to partial Mac conversion
    public class LocalCacheFileSystemContentStoreSingleInstanceTests : SingleInstanceTests
    {
        private const uint MaxStrongFingerprints = 10;
        private static readonly ITestClock Clock = new MemoryClock();

        public LocalCacheFileSystemContentStoreSingleInstanceTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        protected override IStartupShutdown CreateFirstInstance(DisposableDirectory testDirectory, int singleInstanceTimeoutSeconds)
        {
            var rootPath = testDirectory.Path;
            var config = new ContentStoreConfiguration(new MaxSizeQuota("1MB"), singleInstanceTimeoutSeconds: singleInstanceTimeoutSeconds);

            return LocalCache.CreateUnknownContentStoreInProcMemoizationStoreCache(
                Logger,
                rootPath,
                new SQLiteMemoizationStoreConfiguration(rootPath) { MaxRowCount = MaxStrongFingerprints, SingleInstanceTimeoutSeconds = singleInstanceTimeoutSeconds },
                LocalCacheConfiguration.CreateServerDisabled(),
                clock: Clock,
                configurationModel: new ConfigurationModel(config));
        }

        protected override IStartupShutdown CreateSecondInstance(DisposableDirectory testDirectory, int singleInstanceTimeoutSeconds)
        {
            var rootPath = testDirectory.Path;
            var config = new ConfigurationModel(
                new ContentStoreConfiguration(new MaxSizeQuota("1MB"), singleInstanceTimeoutSeconds: singleInstanceTimeoutSeconds),
                ConfigurationSelection.RequireAndUseInProcessConfiguration);

            return new TestFileSystemContentStore(FileSystem, Clock, rootPath, config);
        }
    }
}
