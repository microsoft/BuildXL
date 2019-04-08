// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
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

                await store.PutRandomAsync(Context, contentSize).ShouldBeSuccess();
                triggeredEviction.Should().BeTrue();
            });
        }

        [Theory]
        [InlineData(true, true, true)] // useLegacyQuotaKeeper: true, purgeAtStartup: true, expectedTriggeredEviction: true
        [InlineData(true, false, false)] // useLegacyQuotaKeeper: true, purgeAtStartup: false, expectedTriggeredEviction: false
        [InlineData(false, true, true)] // useLegacyQuotaKeeper: false, purgeAtStartup: true, expectedTriggeredEviction: true
        [InlineData(false, false, false)] // useLegacyQuotaKeeper: false, purgeAtStartup: false, expectedTriggeredEviction: false
        public async Task StartupShouldTriggerPurgeIfConfigured(bool useLegacyQuotaKeeper, bool purgeAtStartup, bool expectedTriggeredEviction)
        {
            // This test makes sure that if configured QuotaKeeper starts purging at startup if
            // the constructed store is full (above soft limit).

            // Using the same test directory for two invocations to reuse the content.
            using (var directory = new DisposableDirectory(FileSystem))
            {
                ContentStoreSettings = new ContentStoreSettings()
                                       {
                                           StartPurgingAtStartup = purgeAtStartup,
                                           UseLegacyQuotaKeeperImplementation = useLegacyQuotaKeeper,
                                       };

                bool triggeredEviction = false;
                var contentSize = ContentSizeToStartSoftPurging(3);
                await TestStore(Context, Clock, directory, async store =>
                                                {
                                                    store.OnLruEnumerationWithTime = hashes =>
                                                                                     {
                                                                                         // Intentionally returning an empty list to avoid purging the content.
                                                                                         return Task.FromResult((IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>)new ContentHashWithLastAccessTimeAndReplicaCount[0]);
                                                                                     };

                                                    using (var pinContext = store.CreatePinContext())
                                                    {
                                                        // Putting the content 3 times to reach the soft limit.
                                                        // The last put will trigger an eviction, but 'OnLruEnumerationWithTime'
                                                        // returns an empty result to avoid purging.
                                                        await PutRandomAndPinAsync(store, contentSize, pinContext);
                                                        await PutRandomAndPinAsync(store, contentSize, pinContext);
                                                        await PutRandomAndPinAsync(store, contentSize, pinContext);
                                                    }
                                                });

                // Running the store for the second time to force purging process to happen during startup.
                await TestStore(
                    Context,
                    Clock,
                    directory,
                    async store =>
                    {
                        // Syncing the store to wait for purging process to finish.
                        await store.SyncAsync(Context);

                        triggeredEviction.Should().Be(expectedTriggeredEviction, "Eviction should be triggered at startup if configured.");
                    },
                    store =>
                        store.OnLruEnumerationWithTime = hashes =>
                                                         {
                                                             triggeredEviction = true;
                                                             return Task.FromResult(hashes);
                                                         }
                );
            }
        }
    }
}
