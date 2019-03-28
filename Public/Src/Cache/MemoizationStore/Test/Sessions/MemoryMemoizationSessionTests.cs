// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class MemoryMemoizationSessionTests : MemoizationSessionTests
    {
        public MemoryMemoizationSessionTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            return new MemoryMemoizationStore(Logger);
        }
    }
}
