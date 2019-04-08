// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Synchronization;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Test.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Test.Synchronization
{
    // ReSharper disable once UnusedMember.Global
    // These tests seem to run infinitely on Mac, likely due to today's partial port
    [Trait("Category", "WindowsOSOnly")]
    public class SQLiteMemoizationStoreSingleInstanceTests : SameSingleInstanceTests
    {
        private const long MaxStrongFingerprints = 10;
        private readonly MemoryClock _clock = new MemoryClock();

        public SQLiteMemoizationStoreSingleInstanceTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        protected override IStartupShutdown CreateInstance(DisposableDirectory testDirectory, int singleInstanceTimeoutSeconds)
        {
            return new TestSQLiteMemoizationStore(
                Logger, _clock, new SQLiteMemoizationStoreConfiguration(testDirectory.Path) { MaxRowCount = MaxStrongFingerprints, SingleInstanceTimeoutSeconds = singleInstanceTimeoutSeconds});
        }

        protected override string TimeoutErrorMessageFragment => $"Failed to acquire single instance lock for {nameof(SQLiteMemoizationStore)}";
    }
}
