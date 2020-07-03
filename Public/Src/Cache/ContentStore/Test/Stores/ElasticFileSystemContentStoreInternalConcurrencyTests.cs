// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Utils;

namespace ContentStoreTest.Stores
{
    public sealed class ElasticFileSystemContentStoreInternalConcurrencyTests : FileSystemContentStoreInternalConcurrencyTests
    {
        protected override TestFileSystemContentStoreInternal Create(AbsolutePath rootPath, ITestClock clock)
        {
            return CreateElastic(rootPath, clock);
        }
    }
}
