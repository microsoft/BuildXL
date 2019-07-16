// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using ContentStoreTest.Test;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class RocksDbMemoizationSessionTests : MemoizationSessionTests
    {
        private readonly MemoryClock _clock = new MemoryClock();

        public RocksDbMemoizationSessionTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            return new RocksDbMemoizationStore(Logger, _clock, new RocksDbContentLocationDatabaseConfiguration(testDirectory.Path));
        }
    }
}
