// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{
    public sealed class ElasticFileSystemContentStoreInternalTests : ContentStoreInternalTests<TestFileSystemContentStoreInternal>
    {
        private static readonly MemoryClock Clock = new MemoryClock();
        private static readonly ContentStoreConfiguration Config = ContentStoreConfiguration.CreateWithElasticSize(initialElasticSizeMegabytes: 1);

        public ElasticFileSystemContentStoreInternalTests(ITestOutputHelper output)
            : base(() => new MemoryFileSystem(Clock), TestGlobal.Logger, output)
        {
        }

        protected override void CorruptContent(TestFileSystemContentStoreInternal store, ContentHash contentHash)
        {
            store.CorruptContent(contentHash);
        }

        protected override TestFileSystemContentStoreInternal CreateStore(DisposableDirectory testDirectory)
        {
            return new TestFileSystemContentStoreInternal(FileSystem, Clock, testDirectory.Path, Config);
        }
    }
}
