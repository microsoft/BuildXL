// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Utils;

namespace ContentStoreTest.Stores
{
    public sealed class ElasticFileSystemContentStoreInternalConcurrencyTests : FileSystemContentStoreInternalConcurrencyTests
    {
        protected override TestFileSystemContentStoreInternal Create(AbsolutePath rootPath, ITestClock clock, NagleQueue<ContentHash> nagleBlock = null)
        {
            return CreateElastic(rootPath, clock, nagleBlock);
        }
    }
}
