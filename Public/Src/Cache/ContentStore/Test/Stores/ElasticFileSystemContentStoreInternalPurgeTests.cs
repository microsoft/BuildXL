// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{
    public class ElasticFileSystemContentStoreInternalPurgeTests : FileSystemContentStoreInternalPurgeTests
    {
        private readonly MaxSizeQuota _quota = new MaxSizeQuota(MaxSizeHard);

        public ElasticFileSystemContentStoreInternalPurgeTests(ITestOutputHelper output)
            : base(() => new MemoryFileSystem(new MemoryClock()), TestGlobal.Logger, output)
        {
        }

        protected ElasticFileSystemContentStoreInternalPurgeTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger)
            : base(createFileSystemFunc, logger)
        {
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

        protected override bool SucceedsEvenIfFull => true;

        [Fact]
        public Task PutTooLargeContentEarlySucceeds()
        {
            return TestStore(Context, Clock, async store =>
            {
                using (MemoryStream originalContentStream = RandomStream(ContentSizeToStartSoftPurging(3)))
                using (MemoryStream tooLargeStream = RandomStream(ContentSizeToStartHardPurging(1)))
                {
                    var triggeredEviction = false;
                    store.OnLruEnumerationWithTime = hashes =>
                    {
                        triggeredEviction = true;
                        return Task.FromResult(hashes);
                    };

                    var putResult = await store.PutStreamAsync(Context, originalContentStream, ContentHashType, null).ShouldBeSuccess();
                    triggeredEviction.Should().BeFalse();
                    var originalContent = putResult.ContentHash;

                    await store.PutStreamAsync(Context, tooLargeStream, ContentHashType, null).ShouldBeSuccess();

                    bool originalContentStillExists = await store.ContainsAsync(Context, originalContent, null);
                    originalContentStillExists.Should().BeFalse();
                }
            });
        }

        [Fact]
        public Task PutTooLargeContentWithExistingContentSucceeds()
        {
            return TestStore(Context, Clock, async store =>
            {
                using (MemoryStream tooLargeStream = RandomStream(ContentSizeToStartHardPurging(1)))
                {
                    var triggeredEviction = false;
                    store.OnLruEnumerationWithTime = hashes =>
                    {
                        triggeredEviction = true;
                        return Task.FromResult(hashes);
                    };
                    await store.PutStreamAsync(Context, tooLargeStream, ContentHashType, null).ShouldBeSuccess();
                    triggeredEviction.Should().BeTrue();
                }
            });
        }

        [Fact]
        public Task PutContentWhenFullSucceeds()
        {
            var contentSize = ContentSizeToStartHardPurging(3);
            return TestStore(Context, Clock, async store =>
            {
                var triggeredEviction = false;
                store.OnLruEnumerationWithTime = hashes =>
                {
                    triggeredEviction = true;
                    return Task.FromResult(hashes);
                };

                using (var pinContext = store.CreatePinContext())
                {
                    await PutRandomAndPinAsync(store, contentSize, pinContext);
                    await PutRandomAndPinAsync(store, contentSize, pinContext);

                    await store.PutRandomAsync(Context, contentSize).ShouldBeSuccess();

                    // Eviction is triggered, but nothing is evicted.
                    triggeredEviction.Should().BeTrue();

                    triggeredEviction = false;

                    await store.PutRandomAsync(Context, contentSize).ShouldBeSuccess();

                    // Eviction is not triggered because quota is now disabled.
                    triggeredEviction.Should().BeFalse();

                    await pinContext.DisposeAsync();
                }
            });
        }

        protected override TestFileSystemContentStoreInternal Create(AbsolutePath rootPath, ITestClock clock, NagleQueue<ContentHash> nagleBlock = null)
        {
            return CreateElastic(rootPath, clock, nagleBlock, _quota);
        }
    }
}
