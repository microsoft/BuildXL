// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStore.Grpc;
using ContentStoreTest.Distributed.ContentLocation.NuCache;
using ContentStoreTest.Distributed.Stores;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class DistributedCentralStorageTests : TestBase
    {
        public DistributedCentralStorageTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        [Theory]
        [InlineData(0, false, false)]
        [InlineData(0, true, false)]
        [InlineData(0, false, true)]
        [InlineData(0, true, true)]
        [InlineData(1, false, false)]
        [InlineData(1, true, false)]
        public async Task FilesRoundTripWithoutQueryingContentLocations(int fileSize, bool isImmutable, bool proactiveCopy)
        {
            var context = new OperationContext(new Context(Logger));
            var (copier, _) = DistributedContentCopierTests.CreateMocks(FileSystem, TestRootDirectoryPath, TimeSpan.Zero);
            await using var copierLifetime = await copier.StartupWithAutoShutdownAsync(context);

            var locationStore = new TestLocationStore();
            var configuration = new DistributedCentralStoreConfiguration(TestRootDirectoryPath / "cache")
            {
                ProactiveCopyCheckpointFiles = proactiveCopy,
                InlineCheckpointProactiveCopies = true,
            };
            var storage = new DistributedCentralStorage(
                configuration,
                locationStore,
                copier,
                new MockCentralStorage(),
                TestSystemClock.Instance,
                preferredContentStore: null);
            await using var storageLifetime = await storage.StartupWithAutoShutdownAsync(context);

            var sourcePath = TestRootDirectoryPath / "source";
            var targetPath = TestRootDirectoryPath / "target";
            var bytes = new byte[fileSize];
            FileSystem.WriteAllBytes(sourcePath, bytes);

            var storageId = await storage.UploadFileAsync(context, sourcePath, "checkpoint").ThrowIfFailureAsync();
            await storage.TouchBlobAsync(context, sourcePath, storageId, isUploader: true, isImmutable).ThrowIfFailureAsync();
            await storage.TryGetFileAsync(context, storageId, targetPath, isImmutable).ThrowIfFailureAsync();

            FileSystem.ReadAllBytes(targetPath).Should().Equal(bytes);
            locationStore.RegistrationCount.Should().Be(fileSize == 0 ? 0 : 3);
            storage.Counters[CentralStorageCounters.TryGetFileFromFallback].Value.Should().Be(0);
        }

        private sealed class TestLocationStore : DistributedCentralStorage.ILocationStore
        {
            public int RegistrationCount { get; private set; }

            public Result<MachineLocation> GetRandomMachineLocation()
            {
                throw new InvalidOperationException("Empty content must not be proactively copied.");
            }

            public Task<GetBulkLocationsResult> GetBulkAsync(OperationContext context, ContentHash hash)
            {
                throw new InvalidOperationException("Empty or locally available content must not require a location query.");
            }

            public ValueTask<BoolResult> RegisterLocalLocationAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentInfo)
            {
                RegistrationCount += contentInfo.Count;
                return new ValueTask<BoolResult>(BoolResult.Success);
            }
        }
    }
}
