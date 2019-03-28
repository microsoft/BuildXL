// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{
    public abstract class FixedSizeFileSystemContentStoreInternalPurgeTests : FileSystemContentStoreInternalPurgeTests
    {
        protected FixedSizeFileSystemContentStoreInternalPurgeTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger, ITestOutputHelper output = null)
            : base(createFileSystemFunc, logger, output)
        {
        }

        [Fact]
        public Task PutTooLargeContentFailsEarly()
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

                    var putResult = await store.PutStreamAsync(Context, originalContentStream, ContentHashType, null);
                    putResult.Succeeded.Should().BeTrue();
                    var originalContent = putResult.ContentHash;

                    putResult = await store.PutStreamAsync(Context, tooLargeStream, ContentHashType, null);
                    putResult.Succeeded.Should().BeFalse();
                    triggeredEviction.Should().BeTrue();

                    bool originalContentStillExists = await store.ContainsAsync(Context, originalContent, null);
                    originalContentStillExists.Should().BeFalse();
                }
            });
        }

        [Fact]
        public Task PutTooLargeContentWithExistingContentFailsEarly()
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
                    var putResult = await store.PutStreamAsync(Context, tooLargeStream, ContentHashType, null);
                    putResult.Succeeded.Should().BeFalse();
                    triggeredEviction.Should().BeTrue();
                }
            });
        }

        [Fact]
        public Task PutContentWhenFullFailsEarly()
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

                    var putResult = await store.PutRandomAsync(Context, contentSize);
                    putResult.ShouldBeError();
                    triggeredEviction.Should().BeTrue();
                    triggeredEviction = false;

                    putResult = await store.PutRandomAsync(Context, contentSize);
                    putResult.ShouldBeError();

                    await pinContext.DisposeAsync();
                }
            });
        }

        [Fact]
        public Task PutContentSucceedsAfterFullnessReleased()
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

                    PutResult putResult = await store.PutRandomAsync(Context, contentSize);
                    putResult.ShouldBeError();
                    triggeredEviction.Should().BeTrue();
                    triggeredEviction = false;

                    await pinContext.DisposeAsync();
                }

                PutResult putAfterReleaseResult = await store.PutRandomAsync(Context, contentSize);
                ResultTestExtensions.ShouldBeSuccess((BoolResult) putAfterReleaseResult);
                triggeredEviction.Should().BeTrue();
            });
        }
    }
}
