// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Utils;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{
    public sealed class ElasticFileSystemContentStoreInternalPutFileTests : FileSystemContentStoreInternalPutFileTests
    {
        /// <inheritdoc />
        public ElasticFileSystemContentStoreInternalPutFileTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public Task PutFileExceedQuotaSucceeds()
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                int size = MaxSizeHard + ValueSize;
                byte[] bytes = ThreadSafeRandom.GetBytes(size);

                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    AbsolutePath pathToContent = tempDirectory.Path / "tempContent.txt";
                    FileSystem.WriteAllBytes(pathToContent, bytes);
                    ContentHash hash;
                    using (var pinContext = store.CreatePinContext())
                    {
                        var r = await store.PutFileAsync(
                            context, pathToContent, FileRealizationMode.Any, ContentHashType, new PinRequest(pinContext)).ShouldBeSuccess();
                        hash = r.ContentHash;
                        Clock.Increment();
                    }

                    Assert.True(await store.ContainsAsync(context, hash, null));

                    // Sync to allow calibration to occur.
                    await store.SyncAsync(context);
                    var currentQuota = await LoadElasticQuotaAsync(store.RootPathForTest);
                    Assert.NotNull(currentQuota);

                    // Calibration should adjust the quota.
                    Assert.True(size < currentQuota.Quota.Hard);
                }
            });
        }

        [Fact]
        public Task SuccessivePutFilesAfterExceedingQuotaSucceed()
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    int totalSize = 0;
                    var hashes = new List<ContentHash>();

                    using (var pinContext = store.CreatePinContext())
                    {
                        int i = 0;

                        while (totalSize < MaxSizeHard * 2)
                        {
                            byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                            totalSize += ValueSize;
                            AbsolutePath pathToContent = tempDirectory.Path / $"tempContent-{i++}.txt";
                            FileSystem.WriteAllBytes(pathToContent, bytes);

                            var r = await store.PutFileAsync(
                                context, pathToContent, FileRealizationMode.Any, ContentHashType, new PinRequest(pinContext)).ShouldBeSuccess();
                            hashes.Add(r.ContentHash);
                            Clock.Increment();
                        }
                    }

                    foreach (var contentHash in hashes)
                    {
                        Assert.True(await store.ContainsAsync(context, contentHash, null));
                    }

                    // Sync to allow calibration to occur.
                    await store.SyncAsync(context);
                    var currentQuota = await LoadElasticQuotaAsync(store.RootPathForTest);
                    Assert.NotNull(currentQuota);

                    // Calibration should adjust the quota.
                    Assert.True(totalSize < currentQuota.Quota.Hard);
                }
            });
        }

        protected override TestFileSystemContentStoreInternal Create(AbsolutePath rootPath, ITestClock clock, NagleQueue<ContentHash> nagleBlock = null)
        {
            return CreateElastic(rootPath, clock, nagleBlock, new MaxSizeQuota(MaxSizeHard));
        }
    }
}
