// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Utilities;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Cache.ContentStore.Sessions.RocksDbFileSystemContentSession;
using FileInfo = System.IO.FileInfo;
using System.Threading;
using BuildXL.Cache.ContentStore.Exceptions;

namespace BuildXL.Cache.ContentStore.Test.Sessions
{
    [Trait("Category", "WindowsOSOnly")]
    [Trait("Category", "Performance")]
    public class RocksDbFileSystemContentSessionTests: ContentSessionTests
    {
        private const long CacheDefaultSize = 100;

        public RocksDbFileSystemContentSessionTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, false, output: output)
        {
        }

        protected override bool RunBulkMethodTests => false;

        protected override bool RunEvictionBasedTests => false;

        /// <summary>
        ///     Creates a RocksDB store
        /// </summary>
        /// <param name="testDirectory">Test Directory</param>
        /// <param name="configuration">Configuration (includes max quota size)</param>
        /// <returns>RocksDB store</returns>
        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            var rootPath = testDirectory.Path;
            return new RocksDbFileSystemContentStore(FileSystem, SystemClock.Instance, rootPath);
        }

        /// <summary>
        ///     Creates a RocksDB store, runs startup, calls a async function, runs shut down
        /// </summary>
        /// <param name="implicitPin">Implicit Pin</param>
        /// <param name="directory">Disposable Directory</param>
        /// <param name="funcAsync">Function that takes a context and a RocksDbFileSystemContentSession and returns a task</param>
        /// <returns>RocksDB store</returns>
        protected async Task RunTestRocksDbAsync(ImplicitPin implicitPin, DisposableDirectory directory, Func<Context, RocksDbFileSystemContentSession, Task> funcAsync)
        {
            // Debugger.Launch();
            var context = new Context(Logger);

            bool useNewDirectory = directory == null;
            if (useNewDirectory)
            {
                directory = new DisposableDirectory(FileSystem);
            }

            try
            {
                var config = CreateStoreConfiguration();

                using (var store = CreateStore(directory, config))
                {
                    try
                    {
                        await store.StartupAsync(context).ShouldBeSuccess();

                        var createResult = store.CreateSession(context, Name, implicitPin).ShouldBeSuccess();
                        using (var session = (RocksDbFileSystemContentSession) createResult.Session)
                        {
                            try
                            {
                                Assert.False(session.StartupStarted);
                                Assert.False(session.StartupCompleted);
                                Assert.False(session.ShutdownStarted);
                                Assert.False(session.ShutdownCompleted);

                                await session.StartupAsync(context).ShouldBeSuccess();

                                await funcAsync(context, session);
                            }
                            finally
                            {
                                await session.ShutdownAsync(context).ShouldBeSuccess();
                            }

                            Assert.True(session.StartupStarted);
                            Assert.True(session.StartupCompleted);
                            Assert.True(session.ShutdownStarted);
                            Assert.True(session.ShutdownCompleted);
                        }
                    }
                    finally
                    {
                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
            }
            finally
            {
                if (useNewDirectory)
                {
                    directory.Dispose();
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(long.MinValue)]
        [InlineData(long.MaxValue)]
        [InlineData(-10)]
        [InlineData(10)]
        public void TestSizeSerializationRoundtrip(long value)
        {
            Span<byte> temporary = stackalloc byte[sizeof(long)];
            RocksDbFileSystemContentStore.SerializeInt64(value, temporary);
            var recovered = RocksDbFileSystemContentStore.DeserializeInt64(temporary);
            Assert.Equal(value, recovered);
        }

        [Fact]
        public void GetAllThreeLetterHexPrefixesTest()
        {
            IEnumerable<string> hashPrefixes = RocksDbFileSystemContentStore.GetAllThreeLetterHexPrefixes();
            Assert.Equal(Math.Pow(16, 3), hashPrefixes.Count());
            Assert.True(hashPrefixes.Contains("1EF"));
            Assert.False(hashPrefixes.Contains("1EF1"));
            Assert.False(hashPrefixes.Contains("1EG"));
        }

        [Fact]
        public async Task OpenRocksDBExistingFileSystemNonExisting()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                await RunTestRocksDbAsync(ImplicitPin.None, directory, async (context, session) =>
                {
                    var pr1 = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    Assert.True(session.TryGetContentMetadata(pr1.ContentHash, out var metadata));
                    Assert.False(pr1.ContentAlreadyExistsInCache);

                    FileSystem.DeleteFile(session.GetPath(pr1.ContentHash));
                    var pr2 = await session.OpenStreamAsync(context, pr1.ContentHash, new CancellationToken()).ShouldBeNotFound();

                    Assert.False(session.TryGetContentMetadata(pr1.ContentHash, out metadata));
                });
            }
        }

        [Fact]
        public async Task PutContentMetadataUpdateCacheSizeFalseAsync()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                await RunTestRocksDbAsync(ImplicitPin.None, directory, (context, session) =>
                {
                    ContentHash hash = new ContentHash(HashType.SHA256);
                    ContentMetadata metadata = new ContentMetadata(CacheDefaultSize, default, default);

                    var oldCacheSize = session.GetCacheSize();
                    session.PutContentMetadata(hash, metadata, updateCacheSize: false);

                    var newCacheSize = session.GetCacheSize();
                    session.TryGetContentMetadata(hash, out var retrievedMetadata);

                    Assert.Equal(oldCacheSize, newCacheSize);
                    Assert.Equal(metadata, retrievedMetadata);
                    return Task.CompletedTask;
                });
            }
        }

        [Fact]
        public async Task PutContentMetadataUpdateCacheSizeTrueAsync()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                await RunTestRocksDbAsync(ImplicitPin.None, directory, (context, session) =>
                {
                    ContentHash hash = new ContentHash(HashType.SHA256);
                    ContentMetadata metadata = new ContentMetadata(CacheDefaultSize, default, default);

                    var oldCacheSize = session.GetCacheSize();
                    session.PutContentMetadata(hash, metadata, updateCacheSize: true);

                    var newCacheSize = session.GetCacheSize();
                    session.TryGetContentMetadata(hash, out var retrievedMetadata);

                    Assert.Equal(oldCacheSize + metadata.Size, newCacheSize);
                    Assert.Equal(metadata, retrievedMetadata);
                    return Task.CompletedTask;
                });
            }
        }

        [Fact]
        public async Task RemoveContentMetadataAsync()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                await RunTestRocksDbAsync(ImplicitPin.None, directory, (context, session) =>
                {
                    ContentHash hash = new ContentHash(HashType.SHA256);
                    ContentMetadata metadata = new ContentMetadata(CacheDefaultSize, default, default);

                    var oldCacheSize = session.GetCacheSize();
                    session.PutContentMetadata(hash, metadata, updateCacheSize: true);

                    session.RemoveContentMetaData(hash);
                    var newCacheSize = session.GetCacheSize();

                    Assert.False(session.TryGetContentMetadata(hash, out var retrievedMetadata));
                    Assert.Equal(oldCacheSize, newCacheSize);
                    return Task.CompletedTask;
                });
            }
        }

        // The tests below are disabled because the methods in RocksDbFileSystemContentSession are not implemented

        [Fact]
        public override Task PinExisting() { return Task.CompletedTask; }

        [Fact]
        public override Task PinNonExisting() { return Task.CompletedTask; }

        [Fact]
        public override Task PlaceFileExisting() { return Task.CompletedTask; }

        [Fact]
        public override Task PlaceFileExistingReplaces() { return Task.CompletedTask; }

        [Fact]
        public override Task PlaceFileFailsIfExists() { return Task.CompletedTask; }

        [Fact]
        public override Task PlaceFileNonExisting() { return Task.CompletedTask; }

        [Fact]
        public override Task PlaceFileSkipsIfExists() { return Task.CompletedTask; }
    }
}
