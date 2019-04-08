// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Stores;
using ContentStoreTest.Test;

namespace ContentStoreTest.Synchronization
{
    // ReSharper disable once UnusedMember.Global
    public class FileSystemContentStoreSingleInstanceTests : SameSingleInstanceTests
    {
        private static readonly ITestClock Clock = new MemoryClock();

        public FileSystemContentStoreSingleInstanceTests()
            : base(() => new MemoryFileSystem(Clock), TestGlobal.Logger)
        {
        }

        protected override IStartupShutdown CreateInstance(DisposableDirectory testDirectory, int singleInstanceTimeoutSeconds)
        {
            var rootPath = testDirectory.Path;
            var config = new ConfigurationModel(
                new ContentStoreConfiguration(new MaxSizeQuota("1MB"), singleInstanceTimeoutSeconds: singleInstanceTimeoutSeconds),
                ConfigurationSelection.RequireAndUseInProcessConfiguration);

            return new TestFileSystemContentStore(FileSystem, Clock, rootPath, config);
        }

        protected override string TimeoutErrorMessageFragment => "Failed to acquire single instance lock";
    }
}
