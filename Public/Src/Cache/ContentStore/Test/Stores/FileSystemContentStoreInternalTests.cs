// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{
    public sealed class FileSystemContentStoreInternalTests : ContentStoreInternalTests<TestFileSystemContentStoreInternal>
    {
        private static readonly MemoryClock Clock = new MemoryClock();
        private static readonly ContentStoreConfiguration Config = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);

        public FileSystemContentStoreInternalTests(ITestOutputHelper output)
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

        [Fact]
        public void PathWithTildaShouldNotCauseArgumentException()
        {
            var path = PathGeneratorUtilities.GetAbsolutePath("e", @".BuildXLCache\Shared\VSO0\364\~DE-1");

            Assert.Throws<CacheException>(() => FileSystemContentStoreInternal.TryGetHashFromPath(new AbsolutePath(path), out _));
        }

        [Fact]
        public async Task TestReconstruction()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var context = new Context(Logger);

                // Create a store with random content
                // Shut down the store correctly
                var store1 = CreateStore(testDirectory);
                await store1.StartupAsync(context).ShouldBeSuccess();
                var putResult = await store1.PutRandomAsync(context, ValueSize).ShouldBeSuccess();

                await store1.ShutdownAsync(context).ShouldBeSuccess();

                // Recreate a store and assert that the content is present
                // Put additional content
                var store2 = CreateStore(testDirectory);
                await store2.StartupAsync(context).ShouldBeSuccess();
                var putResult2 = await store2.PutRandomAsync(context, ValueSize).ShouldBeSuccess();
                // The first content should be in the second store.
                await store2.OpenStreamAsync(context, putResult.ContentHash, null).ShouldBeSuccess();

                // Creating a third store without shutting down the second one.
                var store3 = CreateStore(testDirectory);
                await store3.StartupAsync(context).ShouldBeSuccess();
                // The content from the first and the second stores should be available.
                await store3.OpenStreamAsync(context, putResult.ContentHash, null).ShouldBeSuccess();
                await store3.OpenStreamAsync(context, putResult2.ContentHash, null).ShouldBeSuccess();
            }
        }

        [Fact]
        public async Task TestSelfCheck()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var context = new Context(Logger);

                var store = CreateStore(testDirectory);
                await store.StartupAsync(context).ShouldBeSuccess();
                var putResult = await store.PutRandomAsync(context, ValueSize).ShouldBeSuccess();

                var currentSize = store.ContentDirectorySize();

                var pathInCache = store.GetPrimaryPathFor(putResult.ContentHash);
                FileSystem.WriteAllText(pathInCache, "Definitely wrong content");

                var result = await store.SelfCheckAsync(context, CancellationToken.None).ShouldBeSuccess();

                result.Value.InvalidFiles.Should().Be(1);

                // An invalid file should be removed from:

                // 1. File system
                FileSystem.FileExists(pathInCache).Should().BeFalse("The store should delete the file with invalid content.");

                // 2. Content directory
                store.ContentDirectorySize().Should().Be(currentSize - ValueSize);

                // 3. Quota Keeper
                store.QuotaKeeperSize().Should().Be(currentSize - ValueSize);
            }
        }
    }
}
