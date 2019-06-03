// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

#pragma warning disable SA1402 // File may only contain a single class

namespace ContentStoreTest.Stores
{
    public class MemoryContentDirectoryTests : ContentDirectoryTests
    {
        public MemoryContentDirectoryTests()
            : this(new MemoryClock())
        { }

        private MemoryContentDirectoryTests(MemoryClock clock)
            : base(TestGlobal.Logger, clock, new Lazy<IAbsFileSystem>(() => new MemoryFileSystem(clock)))
        {
        }

        private TestHost Host { get; } = new TestHost();

        protected override IContentDirectory CreateContentDirectory(DisposableDirectory testDirectory)
        {
            return new MemoryContentDirectory(FileSystem, testDirectory.Path, Host);
        }

        [Fact]
        public async Task StartupMovesContentDirectoryFileToBackup()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath filePath = await PopulateAndGetFilePath(context, testDirectory);
                await TestContentDirectory(context, testDirectory, async contentDirectory =>
                {
                    // Forcing initialization to complete.
                    await contentDirectory.EnumerateContentHashesAsync();
                    Assert.False(FileSystem.FileExists(filePath));
                    Assert.True(FileSystem.FileExists(filePath.Parent / MemoryContentDirectory.BinaryBackupFileName));
                });
                Assert.True(FileSystem.FileExists(filePath));
            }
        }

        private class TestHost : IContentDirectoryHost
        {
            public ContentDirectorySnapshot<ContentFileInfo> Content = new ContentDirectorySnapshot<ContentFileInfo>();

            /// <inheritdoc />
            public ContentDirectorySnapshot<ContentFileInfo> Reconstruct(Context context) => Content;
        }

        [Fact]
        public async Task ReconstructionMaintainsLastAccessTimes()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                MemoryClock.Increment();

                // Populate with content
                IReadOnlyList<KeyValuePair<ContentHash, ContentFileInfo>> priorContent = null;
                var filePath = await TestContentDirectory(
                    context,
                    testDirectory,
                    async contentDirectory =>
                    {
                        priorContent = await PopulateRandomInfo(contentDirectory);
                    },
                    shutdown: true);

                MemoryClock.Increment();

                // Populate with content that will be need to be recovered because shutdown is skipped
                IReadOnlyList<KeyValuePair<ContentHash, ContentFileInfo>> reconstructedContent = null;
                await TestContentDirectory(
                    context,
                    testDirectory,
                    async contentDirectory =>
                    {
                        reconstructedContent = await PopulateRandomInfo(contentDirectory);
                        Assert.False(FileSystem.FileExists(filePath));
                        Assert.True(FileSystem.FileExists(filePath.Parent / MemoryContentDirectory.BinaryBackupFileName));
                    },
                    shutdown: false);

                var priorContentMap = priorContent.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                MemoryClock.Increment();
                var allContent = priorContent.Concat(reconstructedContent).ToList();

                Host.Content = new ContentDirectorySnapshot<ContentFileInfo>();
                Host.Content.Add(allContent.Select(kv => new PayloadFromDisk<ContentFileInfo>(kv.Key, kv.Value)));

                // Update last access time to simulate what would happen after reconstruction
                allContent.ForEach(hashInfoPair => hashInfoPair.Value.UpdateLastAccessed(MemoryClock));

                // Check reconstructed content directory
                await TestContentDirectory(
                    context,
                    testDirectory,
                    async contentDirectory =>
                    {
                        // Verify that the full set of content is present in the initialized content directory
                        foreach (var content in allContent)
                        {
                            contentDirectory.TryGetFileInfo(content.Key, out var contentInfo).Should().BeTrue();
                        }

                        foreach (var hash in await contentDirectory.GetLruOrderedCacheContentAsync())
                        {
                            if (!priorContentMap.ContainsKey(hash))
                            {
                                // Prior content should come before the reconstructed content which was preserved in the backup file
                                // because we know the prior content was added prior to the newly discovered content
                                Assert.Empty(priorContentMap);
                            }
                            else
                            {
                                priorContentMap.Remove(hash);
                            }
                        }

                        Assert.False(FileSystem.FileExists(filePath));
                        Assert.True(FileSystem.FileExists(filePath.Parent / MemoryContentDirectory.BinaryBackupFileName));
                    },
                    shutdown: true);

                Assert.True(FileSystem.FileExists(filePath));
            }
        }

        [Fact]
        public async Task ClearedWhenCorrupted()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath databaseFilePath = await PopulateAndGetFilePath(context, testDirectory);
                FileSystem.WriteAllBytes(
                    databaseFilePath, FileSystem.ReadAllBytes(databaseFilePath).Select(b => (byte)(b ^ 0xFF)).ToArray());

                // flip all bytes
                await TestContentDirectory(context, testDirectory, async contentDirectory =>
                    (await contentDirectory.GetCountAsync()).Should().Be(0));
            }
        }

        [Fact]
        public async Task OutOfRangeTimestampsNormalized()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                // Construct a valid directory with some legit entries.
                using (var directory = new MemoryContentDirectory(FileSystem, testDirectory.Path))
                {
                    await directory.StartupAsync(context).ShouldBeSuccess();
                    await PopulateRandomInfo(directory);
                    await directory.ShutdownAsync(context).ShouldBeSuccess();
                }

                // Corrupt it in place with out of range timestamps.
                var i = 0;
                await MemoryContentDirectory.TransformFile(context, FileSystem, testDirectory.Path, pair =>
                {
                    ContentFileInfo fileInfo = pair.Value;
                    var lastAccessedFileTimeUtc = (i++ % 2) == 0 ? -1 : long.MaxValue;
                    var updatedFileInfo = new ContentFileInfo(fileInfo.FileSize, lastAccessedFileTimeUtc, fileInfo.ReplicaCount);
                    return new KeyValuePair<ContentHash, ContentFileInfo>(pair.Key, updatedFileInfo);
                });

                // Load the directory again, fixing the bad timestamps.
                using (var directory = new MemoryContentDirectory(FileSystem, testDirectory.Path))
                {
                    await directory.StartupAsync(context).ShouldBeSuccess();
                    await directory.ShutdownAsync(context).ShouldBeSuccess();
                }

                // Verify timestamps are now in range.
                long nowFileTimeUtc = DateTime.UtcNow.ToFileTimeUtc();
                await MemoryContentDirectory.TransformFile(context, FileSystem, testDirectory.Path, pair =>
                {
                    long fileTimeUtc = pair.Value.LastAccessedFileTimeUtc;
                    fileTimeUtc.Should().BePositive();
                    fileTimeUtc.Should().BeLessOrEqualTo(nowFileTimeUtc);
                    return pair;
                });
            }
        }
    }
}
