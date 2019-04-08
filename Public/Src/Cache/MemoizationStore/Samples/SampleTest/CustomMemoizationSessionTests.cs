// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using ContentStoreTest.Test;
using BuildXL.MemoizationStore.Stores;
using MemoizationStoreInterfaces.Stores;
using MemoizationStoreInterfacesTest.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SampleTest
{
    [TestClass]
    public class CustomMemoizationSessionTests : MemoizationSessionTests
    {
        public CustomMemoizationSessionTests()
            : base(() => new PassThroughFileSystem(), TestGlobal.Logger)
        {
        }

        [TestMethod]
        public void CustomTest()
        {
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            return new MemoryMemoizationStore(Logger);
        }
    }
}
