// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using ContentStoreTest.Test;

// ReSharper disable All
namespace ContentStoreTest.Sessions
{
    public class FileSystemContentSessionTests : ContentSessionTests
    {
        public FileSystemContentSessionTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            var rootPath = testDirectory.Path;
            var configurationModel = new ConfigurationModel(configuration);
            return new FileSystemContentStore(FileSystem, SystemClock.Instance, rootPath, configurationModel);
        }
    }
}
