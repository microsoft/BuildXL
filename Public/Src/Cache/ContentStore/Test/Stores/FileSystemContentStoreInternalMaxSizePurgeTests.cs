// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1402 // File may only contain a single class
#pragma warning disable IDE0040 // Accessibility modifiers required

namespace ContentStoreTest.Stores
{
    public class FileSystemContentStoreInternalMaxSizePurgeTests : FixedSizeFileSystemContentStoreInternalPurgeTests
    {
        private readonly MaxSizeQuota _quota = new MaxSizeQuota(1000);

        public FileSystemContentStoreInternalMaxSizePurgeTests(ITestOutputHelper output = null)
            : base(() => new MemoryFileSystem(new MemoryClock()), TestGlobal.Logger, output)
        {
        }

        protected FileSystemContentStoreInternalMaxSizePurgeTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger, ITestOutputHelper output = null)
            : base(createFileSystemFunc, logger, output)
        {
        }

        protected override TestFileSystemContentStoreInternal Create(AbsolutePath rootPath, ITestClock clock, NagleQueue<ContentHash> nagleBlock = null)
        {
            var config = new ContentStoreConfiguration(_quota);
            return new TestFileSystemContentStoreInternal(FileSystem, clock, rootPath, config, nagleQueue: nagleBlock, settings: ContentStoreSettings);
        }

        protected override int ContentSizeToStartSoftPurging(int numberOfBlobs)
        {
            var size = _quota.Hard;
            size *= Math.Min(_quota.Soft + 5, 100);
            size /= 100;
            size /= numberOfBlobs;
            return (int)size;
        }

        protected override int ContentSizeToStartHardPurging(int numberOfBlobs)
        {
            var size = _quota.Hard + numberOfBlobs;
            size /= numberOfBlobs;
            return (int)size;
        }

        [Fact(Skip = "Flaky")]
        public async Task PutWithMultipleReplicasPurgesLinked()
        {
            var contentSize = ContentSizeToStartSoftPurging(1);

            using (var directory = new DisposableDirectory(FileSystem))
            {
                await TestStore(Context, Clock, async store =>
                {
                    using (MemoryStream stream1 = RandomStream(contentSize), stream2 = RandomStream(contentSize))
                    {
                        var hash1 = await PutAsync(store, stream1);
                        await Replicate(store, hash1, directory, numberOfFiles: 500);
                        var hash2 = await PutAsync(store, stream2, sync: true);
                        await AssertDoesNotContain(store, hash1);
                        await AssertContainsHash(store, hash2);
                    }
                });
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task PutWithReplicaHeldPurgesFor(int replicaIndex)
        {
            var contentSize = ContentSizeToStartSoftPurging(3);

            using (var directory = new DisposableDirectory(FileSystem))
            {
                await TestStore(Context, Clock, async store =>
                {
                    using (MemoryStream stream1 = RandomStream(contentSize), stream2 = RandomStream(contentSize))
                    {
                        var hash1 = await PutAsync(store, stream1);
                        await Replicate(store, hash1, directory);

                        AbsolutePath primaryReplicaPath = store.GetReplicaPathForTest(hash1, 0);
                        AbsolutePath secondaryReplicaPath = store.GetReplicaPathForTest(hash1, 1);
                        Assert.True(FileSystem.FileExists(primaryReplicaPath));
                        Assert.True(FileSystem.FileExists(secondaryReplicaPath));

                        // Make one of the replicas temporarily un-deleteable.
                        AbsolutePath replicaPath = store.GetReplicaPathForTest(hash1, replicaIndex);
                        store.ThrowOnAttemptedDeletePath = replicaPath;
                        {
                            // Force the eviction of one of the replicas.
                            var hash2 = await PutAsync(store, stream2, sync: true);
                            Assert.False(FileSystem.FileExists(primaryReplicaPath));
                            Assert.False(FileSystem.FileExists(secondaryReplicaPath));
                            await AssertDoesNotContain(store, hash1);
                            await AssertContainsHash(store, hash2);
                        }
                        store.ThrowOnAttemptedDeletePath = null;
                    }
                });
            }
        }
    }

    [Trait("Category", "Integration")]
    [Trait("Category", "Integration2")]
    // Disabling test for now
    /*public*/ class FileSystemContentStoreInternalMaxSizePurgeIntegrationTests : FileSystemContentStoreInternalMaxSizePurgeTests
    {
        public FileSystemContentStoreInternalMaxSizePurgeIntegrationTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
            Assert.True(Clock is TestSystemClock);
        }
    }
}
