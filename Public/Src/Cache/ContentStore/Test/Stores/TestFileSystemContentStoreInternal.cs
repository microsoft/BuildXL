// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using FluentAssertions;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace ContentStoreTest.Stores
{
    public sealed class TestFileSystemContentStoreInternal : FileSystemContentStoreInternal, IContentChangeAnnouncer
    {
        public AbsolutePath ThrowOnUpgradeLegacyVsoHashedContentFileRename;
        public AbsolutePath ThrowOnUpgradeLegacyVsoHashedContentDirectoryRename;
        public AbsolutePath ThrowOnUpgradeLegacyVsoHashedContentDirectoryDelete;
        public AbsolutePath ThrowOnAttemptedDeletePath;
        public bool DeleteContentDirectoryAfterDispose;
        public Func<IReadOnlyList<ContentHash>, Task<IReadOnlyList<ContentHash>>> OnLruEnumeration;
        public Func<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>, Task<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>>> OnLruEnumerationWithTime;
        private const HashType ContentHashType = HashType.Vso0;
        private readonly Action<ContentHashWithSize> _onContentAdded;
        private readonly Action<ContentHashWithSize> _onContentEvicted;

        public TestFileSystemContentStoreInternal(
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath rootPath,
            ContentStoreConfiguration configuration,
            Action<ContentHashWithSize> onContentAdded = null,
            Action<ContentHashWithSize> onContentEvicted = null,
            NagleQueue<ContentHash> nagleQueue = null,
            ContentStoreSettings settings = null,
            DistributedEvictionSettings distributedEvictionSettings = null)
            : base(fileSystem, clock, rootPath, new ConfigurationModel(configuration), nagleQueue: nagleQueue, settings: settings, distributedEvictionSettings: distributedEvictionSettings)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(clock != null);
            Contract.Requires(rootPath != null);
            Contract.Requires(configuration != null);

            _onContentAdded = onContentAdded;
            _onContentEvicted = onContentEvicted;

            if (_onContentAdded != null || _onContentEvicted != null)
            {
                Announcer = this;
            }
        }

        protected override void DisposeCore()
        {
            base.DisposeCore();

            if (DeleteContentDirectoryAfterDispose)
            {
                FileSystem.DeleteFile(ContentDirectory.FilePath);
            }
        }

        public static int CurrentVersionNumber => (int)VersionHistory.CurrentVersion;

        public long ContentDirectorySize() => ContentDirectory.GetSizeAsync().GetAwaiter().GetResult();

        public long QuotaKeeperSize() => QuotaKeeper?.CurrentSize ?? 0;

        public AbsolutePath RootPathForTest => RootPath;

        public SerializedDataValue SerializedDataVersionForTest => SerializedDataVersion;

        public AbsolutePath VersionFilePathForTest => VersionFilePath;

        public ConcurrentDictionary<ContentHash, Pin> PinMapForTest => PinMap;

        public IContentDirectory ContentDirectoryForTest => ContentDirectory;

        public void CorruptContent(ContentHash contentHash)
        {
            DeleteReadOnlyFile(GetPrimaryPathFor(contentHash));
            FileSystem.WriteAllBytes(GetPrimaryPathFor(contentHash), new byte[] {0});
        }

        public async Task<ContentFileInfo> GetCacheFileInfo(ContentHash contentHash)
        {
            ContentFileInfo info = null;

            await ContentDirectory.UpdateAsync(contentHash, false, Clock, fileInfo =>
            {
                info = fileInfo;
                return null;
            });
            return info;
        }

        internal AbsolutePath GetReplicaPathForTest(ContentHash contentHash, int replicaIndex)
        {
            return GetReplicaPathFor(contentHash, replicaIndex);
        }

        public Task ContentAdded(ContentHashWithSize item)
        {
            _onContentAdded?.Invoke(item);
            return Task.FromResult(0);
        }

        public Task ContentEvicted(ContentHashWithSize item)
        {
            _onContentEvicted?.Invoke(item);
            return Task.FromResult(0);
        }

        protected override void UpgradeLegacyVsoContentRenameDirectory(AbsolutePath sourcePath, AbsolutePath destinationPath)
        {
            if (ThrowOnUpgradeLegacyVsoHashedContentDirectoryRename == sourcePath)
            {
                throw new IOException();
            }

            base.UpgradeLegacyVsoContentRenameDirectory(sourcePath, destinationPath);
        }

        protected override void UpgradeLegacyVsoContentRenameFile(AbsolutePath sourcePath, AbsolutePath destinationPath)
        {
            if (ThrowOnUpgradeLegacyVsoHashedContentFileRename == sourcePath)
            {
                throw new IOException();
            }

            base.UpgradeLegacyVsoContentRenameFile(sourcePath, destinationPath);
        }

        protected override void UpgradeLegacyVsoContentDeleteDirectory(AbsolutePath directory)
        {
            if (ThrowOnUpgradeLegacyVsoHashedContentDirectoryDelete == directory)
            {
                throw new IOException();
            }

            base.UpgradeLegacyVsoContentDeleteDirectory(directory);
        }

        protected override void DeleteReadOnlyFile(AbsolutePath path)
        {
            if (path == ThrowOnAttemptedDeletePath)
            {
                throw new IOException();
            }

            base.DeleteReadOnlyFile(path);
        }

        public override async Task<IReadOnlyList<ContentHash>> GetLruOrderedContentListAsync()
        {
            IReadOnlyList<ContentHash> list = await base.GetLruOrderedContentListAsync();
            if (OnLruEnumeration != null)
            {
                list = await OnLruEnumeration(list);
            }

            return list;
        }

        public override async Task<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>> GetLruOrderedContentListWithTimeAsync()
        {
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> list = await base.GetLruOrderedContentListWithTimeAsync();
            if (OnLruEnumerationWithTime != null)
            {
                list = await OnLruEnumerationWithTime(list);
            }

            return list;
        }

        public async Task<byte[]> CorruptWithContentDirectoryEntryForNonexistentBlobAsync()
        {
            var bytes = ThreadSafeRandom.GetBytes(100);
            var contentHash = bytes.CalculateHash(ContentHashType);
            await ContentDirectory.UpdateAsync(contentHash, false, Clock, fileInfo =>
                Task.FromResult(new ContentFileInfo(Clock, 1, 100)));
            (await ContentDirectory.GetCountAsync()).Should().Be(1);
            return bytes;
        }

        public Task<byte[]> CorruptWithExtraReplicaAsync(Context context, MemoryClock clock, DisposableDirectory tempDirectory)
        {
            return CorruptStoreWithReplicasAsync(context, clock, tempDirectory, async hash =>
                await ContentDirectory.UpdateAsync(hash, true, Clock, fileInfo =>
                {
                    fileInfo.ReplicaCount.Should().Be(2);
                    fileInfo.ReplicaCount--;
                    return Task.FromResult(fileInfo);
                }));
        }

        public Task<byte[]> CorruptWithMissingReplicaAsync(Context context, MemoryClock clock, DisposableDirectory tempDirectory)
        {
            return CorruptStoreWithReplicasAsync(context, clock, tempDirectory, hash =>
            {
                var pathForSecondReplica = GetReplicaPathForTest(hash, 1);
                FileSystem.DeleteFile(pathForSecondReplica);
                return Task.FromResult(true);
            });
        }

        public Task<byte[]> CorruptWithCorruptedBlob(Context context, DisposableDirectory tempDirectory)
        {
            return CorruptStoreAsync(context, tempDirectory, contentHash =>
            {
                CorruptContent(contentHash);
                return Task.FromResult(true);
            });
        }

        public Task<byte[]> CorruptWithBlobForNonexistentContentDirectoryEntry(
            Context context, DisposableDirectory tempDirectory)
        {
            return CorruptStoreAsync(context, tempDirectory, async contentHash =>
            {
                await ContentDirectory.RemoveAsync(contentHash);
                (await ContentDirectory.GetCountAsync()).Should().Be(0);
            });
        }

        public async Task EnsureHasContent(Context context, ContentHash contentHash, DisposableDirectory tempDirectory)
        {
            // Ensure the cache now has the just-put content, by placing it.
            var placePath = tempDirectory.CreateRandomFileName();
            var result = await PlaceFileAsync(
                context, contentHash, placePath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.Any, null);
            result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);
        }

        public async Task EnsureContentIsPinned(Context context, MemoryClock clock, ContentHash contentHash)
        {
            Assert.True(await ContainsAsync(context, contentHash, null));
            clock.Increment();

            await ClearStoreOfUnpinnedContent(context, clock);

            Assert.True(await ContainsAsync(context, contentHash, null));
            clock.Increment();
        }

        public async Task EnsureContentIsNotPinned(Context context, MemoryClock clock, ContentHash contentHash)
        {
            Assert.True(await ContainsAsync(context, contentHash, null));
            clock.Increment();

            await ClearStoreOfUnpinnedContent(context, clock);

            Assert.False(await ContainsAsync(context, contentHash, null));
            clock.Increment();
        }

        private async Task ClearStoreOfUnpinnedContent(Context context, MemoryClock clock)
        {
            const int contentsToAdd = 4;
            for (int i = 0; i < contentsToAdd; i++)
            {
                var data = ThreadSafeRandom.GetBytes((int)(Configuration.MaxSizeQuota.Hard / (contentsToAdd - 1)));
                using (var dataStream = new MemoryStream(data))
                {
                    var r = await PutStreamAsync(context, dataStream, ContentHashType, null);
                    var hashFromPut = r.ContentHash;
                    clock.Increment();
                    Assert.True(await ContainsAsync(context, hashFromPut, null));
                    clock.Increment();
                }
            }
        }

        private Task<byte[]> CorruptStoreWithReplicasAsync(
            Context context,
            MemoryClock clock,
            DisposableDirectory tempDirectory,
            Func<ContentHash, Task> corruptFunc)
        {
            return CorruptStoreAsync(context, tempDirectory, async contentHash =>
            {
                // ReSharper disable once UnusedVariable
                foreach (var x in Enumerable.Range(0, 1500))
                {
                    AbsolutePath tempPath = tempDirectory.CreateRandomFileName();
                    clock.Increment();
                    var result = await PlaceFileAsync(
                        context,
                        contentHash,
                        tempPath,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.FailIfExists,
                        FileRealizationMode.HardLink,
                        null);
                    result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);
                }

                await corruptFunc(contentHash);
            });
        }

        private async Task<byte[]> CorruptStoreAsync(
            Context context, DisposableDirectory tempDirectory, Func<ContentHash, Task> corruptFunc)
        {
            // Put a blob into the cache.
            var bytes = ThreadSafeRandom.GetBytes(100);
            var contentHash = bytes.CalculateHash(ContentHashType);
            var contentPath = tempDirectory.CreateRandomFileName();
            FileSystem.WriteAllBytes(contentPath, bytes);
            await PutFileAsync(context, contentPath, FileRealizationMode.Any, contentHash.HashType, null).ShouldBeSuccess();

            await corruptFunc(contentHash);

            return bytes;
        }
    }
}
