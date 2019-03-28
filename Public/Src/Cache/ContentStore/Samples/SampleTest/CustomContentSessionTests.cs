// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.ContentStore.FileSystem;
using BuildXL.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using ContentStoreTest.Test;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SampleTest
{
    [TestClass]
    public class CustomContentSessionTests : ContentSessionTests
    {
        public CustomContentSessionTests()
            : base(() => new PassThroughFileSystem(), TestGlobal.Logger)
        {
        }

        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            return new FileSystemContentStore(FileSystem, Logger, SystemClock.Instance, testDirectory.Path, new ConfigurationModel(configuration));
        }

        [TestMethod]
        public void CustomTest()
        {
        }
    }
}
