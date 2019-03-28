// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{
    public class FileSystemContentStoreInternalDiskFreePercentPurgeTests : FixedSizeFileSystemContentStoreInternalPurgeTests
    {
        private const long VolumeSize = 1000;
        private readonly DiskFreePercentQuota _quota = new DiskFreePercentQuota();

        public FileSystemContentStoreInternalDiskFreePercentPurgeTests(ITestOutputHelper output)
            : base(() => new MyMemoryFileSystem(new MemoryClock(), VolumeSize), TestGlobal.Logger, output)
        {
        }

        protected override TestFileSystemContentStoreInternal Create(AbsolutePath rootPath, ITestClock clock, NagleQueue<ContentHash> nagleBlock = null)
        {
            Contract.Assert(ReferenceEquals(clock, Clock));
            var config = new ContentStoreConfiguration(null, _quota);
            return new TestFileSystemContentStoreInternal(
                FileSystem,
                clock,
                rootPath,
                config,
                contentHashWithSize => ((MyMemoryFileSystem)FileSystem).ContentAdded(contentHashWithSize.Size),
                contentHashWithSize => ((MyMemoryFileSystem)FileSystem).ContentEvicted(contentHashWithSize.Size),
                nagleBlock
                );
        }

        protected override int ContentSizeToStartSoftPurging(int numberOfBlobs)
        {
            return (int)_quota.Hard.PercentFreeToUsedSize(VolumeSize) / numberOfBlobs;
        }

        protected override int ContentSizeToStartHardPurging(int numberOfBlobs)
        {
            var size = _quota.Hard.PercentFreeToUsedSize(VolumeSize) + numberOfBlobs;
            size /= numberOfBlobs;
            return (int)size;
        }

        private class MyMemoryFileSystem : MemoryFileSystem
        {
            private long _usedSpace;

            public MyMemoryFileSystem(ITestClock clock, long volumeSize)
                : base(clock, volumeSize)
            {
            }

            public void ContentAdded(long size)
            {
                Interlocked.Add(ref _usedSpace, size);
            }

            public void ContentEvicted(long size)
            {
                Interlocked.Add(ref _usedSpace, -1 * size);
                Contract.Assert(_usedSpace >= 0);
            }

            public override VolumeInfo GetVolumeInfo(AbsolutePath path)
            {
                var usedSpace = Interlocked.Read(ref _usedSpace);
                var freeSpace = VolumeSize - usedSpace;
                return new VolumeInfo(VolumeSize, freeSpace);
            }
        }

        [Fact]
        public async Task PutWithMultipleReplicasDoesNotPurgeLinked()
        {
            var contentSize = ContentSizeToStartSoftPurging(3);

            using (var directory = new DisposableDirectory(FileSystem))
            {
                await TestStore(Context, Clock, async store =>
                {
                    using (MemoryStream stream1 = RandomStream(contentSize), stream2 = RandomStream(contentSize))
                    {
                        Logger.Debug("Before PutAsync 1");
                        var hash1 = await PutAsync(store, stream1);

                        Logger.Debug("Before Replicate");
                        await Replicate(store, hash1, directory);

                        Logger.Debug("Before PutAsync 2");
                        var hash2 = await PutAsync(store, stream2, sync: true);

                        Logger.Debug("After PutAsync 2");
                        await AssertContainsHash(store, hash1);

                        // Can not assert that hash2 that was added last is presented or not.
                        // In the new quota keeper implementation there is no guarantee that the 'hash2' is not in the store.
                        // Adding 'hash2' triggers asynchronous purge operation because soft limit is surpassed,
                        // but 'hash2' could be available or not in a list of candidates for eviction depending on how
                        // fast the reservation transaction completes.
                        // Legacy quota keeper was evicting content one by one and the race condition was not presented there.
                        // await AssertDoesNotContain(store, hash2);
                    }
                });
            }
        }

        [Fact]
        public async Task PurgeCannotHitTargetWithReplicasMultiplyLinked()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                await TestStore(Context, Clock, async store =>
                {
                    // Pick a size that will trigger purge.
                    var contentSize = _quota.Soft.PercentFreeToUsedSize(VolumeSize);

                    // Pick a size that can replicate multiple times and stay well under soft limit.
                    var smallSize = contentSize / 10;

                    using (MemoryStream
                        stream1 = RandomStream(smallSize),
                        stream2 = RandomStream(smallSize),
                        stream3 = RandomStream(contentSize))
                    {
                        // Put some content without triggering purge.
                        var hash1 = await PutAsync(store, stream1);
                        var hash2 = await PutAsync(store, stream2);

                        // Create external links to this content, still not triggering purge.
                        await Replicate(store, hash1, directory);
                        await Replicate(store, hash2, directory);

                        // Attempt to add more content, initiating a purge, but ultimately failing.
                        var r = await PutStreamAsync(store, stream3);
                        r.Succeeded.Should().BeFalse("This put should fail because not enough space can be purged");

                        // Wait for purging to complete.
                        await store.SyncAsync(Context);

                        var hasHash1 = await ContainsAsync(store, hash1);
                        hasHash1.Should().BeTrue("Should not have been purged because replicas have multiple links");
                    }
                });
            }
        }
    }
}
