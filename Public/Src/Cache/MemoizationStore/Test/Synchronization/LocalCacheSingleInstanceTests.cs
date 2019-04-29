// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Synchronization;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Test.Synchronization
{
    // ReSharper disable once UnusedMember.Global
    [Trait("Category", "WindowsOSOnly")] // Likely failing in OSX due to partial Mac conversion
    public class LocalCacheSingleInstanceTests : SameSingleInstanceTests
    {
        private const uint MaxStrongFingerprints = 10;
        private static readonly IClock Clock = new MemoryClock();

        public LocalCacheSingleInstanceTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        protected override IStartupShutdown CreateInstance(DisposableDirectory testDirectory, int singleInstanceTimeoutSeconds)
        {
            var rootPath = testDirectory.Path;
            var config = new ContentStoreConfiguration(new MaxSizeQuota("1MB"), singleInstanceTimeoutSeconds: singleInstanceTimeoutSeconds);

            return new LocalCache(
                Logger,
                rootPath,
                new SQLiteMemoizationStoreConfiguration(rootPath) { MaxRowCount = MaxStrongFingerprints, SingleInstanceTimeoutSeconds = singleInstanceTimeoutSeconds },
                LocalCacheConfiguration.CreateServerDisabled(),
                clock: Clock,
                configurationModel: new ConfigurationModel(config));
        }

        protected override string TimeoutErrorMessageFragment => $"Failed to acquire single instance lock for {nameof(FileSystemContentStore)}";
    }
}
