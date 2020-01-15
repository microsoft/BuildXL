// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using ContentStoreTest.Distributed.ContentLocation;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.Stores
{
    public class DistributedContentCopierTests : TestBase
    {
        public DistributedContentCopierTests()
            : base(() => new MemoryFileSystem(TestSystemClock.Instance), TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task CopyFailsForWrongCopySize()
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var(distributedCopier, mockFileCopier) = await CreateAsync(context, directory.Path, TimeSpan.Zero);

                var hash = VsoHashInfo.Instance.EmptyHash;
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 42,
                    new MachineLocation[] {new MachineLocation("")});

                mockFileCopier.CopyToAsyncResult = CopyFileResult.SuccessWithSize(41);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 42))
                );

                result.ShouldBeError();
                result.ErrorMessage.Should().Contain("size");
                result.ErrorMessage.Should().Contain("mismatch");
            }
        }

        [Fact]
        public async Task CopyFailsForWrongHash()
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var(distributedCopier, mockFileCopier) = await CreateAsync(context, directory.Path, TimeSpan.Zero);

                var hash = ContentHash.Random();
                var wrongHash = VsoHashInfo.Instance.EmptyHash;
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 42,
                    new MachineLocation[] {new MachineLocation("")});

                mockFileCopier.CopyToAsyncResult = CopyFileResult.SuccessWithSize(42);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(wrongHash, 42))
                );

                result.ShouldBeError();
                result.ErrorMessage.Should().Contain(hash.ToShortString());
                result.ErrorMessage.Should().Contain(wrongHash.ToShortString());
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        public async Task CopyRetries(int retries)
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var (distributedCopier, mockFileCopier) = await CreateAsync(context, directory.Path,TimeSpan.Zero, retries);
                var machineLocations = new MachineLocation[] {new MachineLocation("")};

                var hash = ContentHash.Random();
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 99,
                    machineLocations);

                mockFileCopier.CopyToAsyncResult = new CopyFileResult(CopyFileResult.ResultCode.SourcePathError);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 99)));

                result.ShouldBeError();
                mockFileCopier.CopyAttempts.Should().Be(retries);
            }
        }

        [Theory]
        [InlineData(3)]
        ///<summary>
        /// Test case for bug <"https://dev.azure.com/mseng/1ES/_boards/board/t/DavidW%20-%20Team/Stories/?workitem=1654106"/>
        /// During the first attempt of copying from a list of locations, one of the locations returns a DestinationPathError.
        /// Then in subsequent attempts to copy from the list of locations, the previous location that returned DestinationPathError now returns a different error.
        /// We should still be able to attempt to copy and return without and out of range exception thrown.
        ///</summary>
        public async Task CopyWithDestinationPathError(int retries)
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var (distributedCopier, mockFileCopier) = await CreateAsync(context, directory.Path, TimeSpan.FromMilliseconds((10)), retries);
                var machineLocations = new MachineLocation[] { new MachineLocation(""), new MachineLocation("") };

                var hash = ContentHash.Random();
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 99,
                    machineLocations);
                mockFileCopier.CopyAttempts = 0;
                var totalCopyAttempts = (retries - 1) * machineLocations.Length + 1;
                mockFileCopier.CustomResults = new CopyFileResult[totalCopyAttempts];
                mockFileCopier.CustomResults[0] = new CopyFileResult(CopyFileResult.ResultCode.DestinationPathError);
                for(int counter = 1; counter < totalCopyAttempts; counter ++)
                {
                    mockFileCopier.CustomResults[counter] = new CopyFileResult(CopyFileResult.ResultCode.SourcePathError);
                };
                var destinationResult = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 99)));

                destinationResult.ShouldBeError();
                destinationResult.ErrorMessage.Should().Contain(hash.ToShortString());
                mockFileCopier.CopyAttempts.Should().Be(totalCopyAttempts);
            }
        }

        private async Task<(DistributedContentCopier<AbsolutePath>, MockFileCopier)> CreateAsync(Context context, AbsolutePath rootDirectory, TimeSpan retryInterval, int retries = 1)
        {
            var mockFileCopier = new MockFileCopier();
            var existenceChecker = new TestFileCopier();
            var contentCopier = new TestDistributedContentCopier(
                rootDirectory,
                // Need to use exactly one retry.
                new DistributedContentStoreSettings() { RetryIntervalForCopies = Enumerable.Range(0, retries).Select(r => retryInterval).ToArray() },
                FileSystem,
                mockFileCopier,
                existenceChecker,
                copyRequester: null,
                new NoOpPathTransformer(rootDirectory),
                new MockContentLocationStore());
            await contentCopier.StartupAsync(context).ThrowIfFailure();
            return (contentCopier, mockFileCopier);
        }

        private class TestDistributedContentCopier : DistributedContentCopier<AbsolutePath>
        {
            public TestDistributedContentCopier(
            AbsolutePath workingDirectory,
            DistributedContentStoreSettings settings,
            IAbsFileSystem fileSystem,
            IFileCopier<AbsolutePath> fileCopier,
            IFileExistenceChecker<AbsolutePath> fileExistenceChecker,
            IContentCommunicationManager copyRequester,
            IPathTransformer<AbsolutePath> pathTransformer,
            IContentLocationStore contentLocationStore)
                : base(workingDirectory, settings, fileSystem, fileCopier, fileExistenceChecker, copyRequester, pathTransformer, TestSystemClock.Instance, contentLocationStore)
            {
            }

            protected override Task<CopyFileResult> CopyFileAsync(IFileCopier<AbsolutePath> copier, AbsolutePath sourcePath, AbsolutePath destinationPath, long expectedContentSize, bool overwrite, CancellationToken cancellationToken)
            {
                return copier.CopyToAsync(sourcePath, null, expectedContentSize, cancellationToken);
            }
        }

        private class NoOpPathTransformer : TestPathTransformer
        {
            private readonly AbsolutePath _root;

            public NoOpPathTransformer(AbsolutePath root)
            {
                _root = root;
            }
            public override AbsolutePath GeneratePath(ContentHash contentHash, byte[] contentLocationIdContent)
            {
                return _root;
            }
        }

        private class MockContentLocationStore : IContentLocationStore
        {
            /// <inheritdoc />
            public bool StartupCompleted => false;

            /// <inheritdoc />
            public bool StartupStarted => false;

            /// <inheritdoc />
            public Task<BoolResult> StartupAsync(Context context) => null;

            /// <inheritdoc />
            public void Dispose()
            {
            }

            /// <inheritdoc />
            public bool ShutdownCompleted => false;

            /// <inheritdoc />
            public bool ShutdownStarted => false;

            /// <inheritdoc />
            public Task<BoolResult> ShutdownAsync(Context context) => null;

            /// <inheritdoc />
            public MachineReputationTracker MachineReputationTracker => null;

            /// <inheritdoc />
            public Task<BoolResult> UpdateBulkAsync(
                Context context,
                IReadOnlyList<ContentHashWithSizeAndLocations> contentHashesWithSizeAndLocations,
                CancellationToken cts,
                UrgencyHint urgencyHint,
                LocationStoreOption locationStoreOption) =>
                null;

            /// <inheritdoc />
            public void ReportReputation(MachineLocation location, MachineReputation reputation) { }

            /// <inheritdoc />
            public Task<GetBulkLocationsResult> GetBulkAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint, GetBulkOrigin origin) => null;

            /// <inheritdoc />
            public Task<BoolResult> TrimBulkAsync(Context context, IReadOnlyList<ContentHashAndLocations> contentHashToLocationMap, CancellationToken cts, UrgencyHint urgencyHint) => null;

            /// <inheritdoc />
            public Task<BoolResult> InvalidateLocalMachineAsync(Context context, ILocalContentStore localStore, CancellationToken cts) => null;

            /// <inheritdoc />
            public Task<BoolResult> GarbageCollectAsync(OperationContext context) => null;

            /// <inheritdoc />
            public Task<BoolResult> TrimBulkAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint) => null;

            /// <inheritdoc />
            public Task<ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>>> TrimOrGetLastAccessTimeAsync(Context context, IList<Tuple<ContentHashWithLastAccessTimeAndReplicaCount, bool>> contentHashesWithInfo, CancellationToken cts, UrgencyHint urgencyHint) => null;

            /// <inheritdoc />
            public Task<BoolResult> TouchBulkAsync(Context context, IReadOnlyList<ContentHashWithSize> contentHashes, CancellationToken cts, UrgencyHint urgencyHint) => null;

            /// <inheritdoc />
            public int PageSize => 0;

            /// <inheritdoc />
            public CounterSet GetCounters(Context context) => null;

            /// <inheritdoc />
            public Task<BoolResult> RegisterLocalLocationAsync(Context context, IReadOnlyList<ContentHashWithSize> contentHashes, CancellationToken cts, UrgencyHint urgencyHint, bool touch) => null;

            /// <inheritdoc />
            public Task<BoolResult> PutBlobAsync(OperationContext context, ContentHash contentHash, byte[] blob) => null;

            /// <inheritdoc />
            public Task<GetBlobResult> GetBlobAsync(OperationContext context, ContentHash contentHash) => null;

            /// <inheritdoc />
            public Result<MachineLocation> GetRandomMachineLocation(IReadOnlyList<MachineLocation> except) => default;

            /// <inheritdoc />
            public bool IsMachineActive(MachineLocation machine) => false;

            public Result<MachineLocation[]> GetDesignatedLocations(ContentHash hash)
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc />
            public bool AreBlobsSupported => false;

            /// <inheritdoc />
            public long MaxBlobSize => 0;
        }

        private class MockPathTransformer : IPathTransformer
        {
            /// <inheritdoc />
            public byte[] GetLocalMachineLocation(AbsolutePath cacheRoot) => new byte[] { };

            /// <inheritdoc />
            public PathBase GeneratePath(ContentHash contentHash, byte[] contentLocationIdContent) => null;

            /// <inheritdoc />
            public byte[] GetPathLocation(PathBase path) => new byte[] { };
        }

        private class MockFileCopier : IFileCopier
        {
            public int CopyAttempts = 0;
#pragma warning disable 649
            public CopyFileResult CopyToAsyncResult;
#pragma warning restore 649
            public CopyFileResult[] CustomResults;

            /// <inheritdoc />
            public Task<CopyFileResult> CopyToAsync(PathBase sourcePath, Stream destinationStream, long expectedContentSize, CancellationToken cancellationToken)
            {
                CopyAttempts++;
                if (CustomResults != null)
                {
                    return Task.FromResult(CustomResults[CopyAttempts - 1]);
                }
                return Task.FromResult(CopyToAsyncResult);
            }

#pragma warning disable 649
            public FileExistenceResult CheckFileExistsAsyncResult;
#pragma warning restore 649

            /// <inheritdoc />
            public Task<FileExistenceResult> CheckFileExistsAsync(PathBase path, TimeSpan timeout, CancellationToken cancellationToken)
                => Task.FromResult(CheckFileExistsAsyncResult);
        }
    }
}
