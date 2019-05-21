// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{

    public sealed class FileSystemContentStoreInternalStressTests : ContentStoreInternalTests<TestFileSystemContentStoreInternal>
    {
        private static readonly MemoryClock Clock = new MemoryClock();
        private static readonly ContentStoreConfiguration Config = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);

        public FileSystemContentStoreInternalStressTests(ITestOutputHelper output)
            : base(() => new MockFileSystem(Clock), TestGlobal.Logger, output)
        {
        }

        protected override TestFileSystemContentStoreInternal CreateStore(DisposableDirectory testDirectory)
        {
            return new TestFileSystemContentStoreInternal(FileSystem, Clock, testDirectory.Path, Config);
        }

        private static readonly int OOMEnumerationFiles = 40_000_000;

        private MockFileSystem MockFileSystem => (MockFileSystem)FileSystem;

        [Fact(Skip = "Stress test, meant to be run by hand as it takes several minutes")]
        public async Task TestAvoidOOMOnLargeEnumeration()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var context = new Context(Logger);
                var store = CreateStore(testDirectory);
                await store.StartupAsync(context).ThrowIfFailure();

                MockFileSystem.EnumerateFilesResult = EnumerateFiles(testDirectory.Path);
                store.ReadSnapshotFromDisk(context);
                MockFileSystem.EnumerateFilesResult = null;
            }
        }

        private IEnumerable<FileInfo> EnumerateFiles(AbsolutePath root)
        {
            for (var i = 0; i < OOMEnumerationFiles; i++)
            {
                var hash = ContentHash.Random().ToHex();
                string text = i.ToString();
                
                yield return new FileInfo {
                    FullPath = root / "VSO0" / $"{hash.Substring(0, 3)}" / $"{hash}.blob",
                    Length = 1,
                };
            }
        }

        protected override void CorruptContent(TestFileSystemContentStoreInternal store, ContentHash contentHash)
        {
            store.CorruptContent(contentHash);
        }
    }
}
