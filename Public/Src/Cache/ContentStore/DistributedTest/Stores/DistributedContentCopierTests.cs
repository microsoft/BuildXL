// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
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
                var(distributedCopier, mockFileCopier) = await CreateAsync(context, directory.Path);

                var hash = ContentHash.Random();
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 42,
                    new MachineLocation[] {new MachineLocation("")});

                mockFileCopier.CopyFileAsyncResult = CopyFileResult.SuccessWithSize(41);
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
                var(distributedCopier, mockFileCopier) = await CreateAsync(context, directory.Path);

                var hash = ContentHash.Random();
                var wrongHash = ContentHash.Random();
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 42,
                    new MachineLocation[] {new MachineLocation("")});

                mockFileCopier.CopyFileAsyncResult = CopyFileResult.SuccessWithSize(42);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(wrongHash, 42))
                );

                result.ShouldBeError();
                result.ErrorMessage.Should().Contain(hash.ToString());
                result.ErrorMessage.Should().Contain(wrongHash.ToString());
            }
        }

        private async Task<(DistributedContentCopier<AbsolutePath>, MockFileCopier)> CreateAsync(Context context, AbsolutePath rootDirectory)
        {
            var mockFileCopier = new MockFileCopier();
            var existenceChecker = new TestFileCopier();
            var contentCopier = new DistributedContentCopier<AbsolutePath>(
                rootDirectory,
                // Need to use exactly one retry.
                new DistributedContentStoreSettings(){RetryIntervalForCopies = new TimeSpan[]{TimeSpan.Zero}},
                FileSystem,
                mockFileCopier,
                existenceChecker,
                new NoOpPathTransformer(rootDirectory),
                new MockContentLocationStore());
            await contentCopier.StartupAsync(context).ThrowIfFailure();
            return (contentCopier, mockFileCopier);
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
            public Task<BoolResult> RegisterLocalLocationAsync(Context context, IReadOnlyList<ContentHashWithSize> contentHashes, CancellationToken cts, UrgencyHint urgencyHint) => null;

            /// <inheritdoc />
            public Task<BoolResult> PutBlobAsync(OperationContext context, ContentHash contentHash, byte[] blob) => null;

            /// <inheritdoc />
            public Task<Result<byte[]>> GetBlobAsync(OperationContext context, ContentHash contentHash) => null;

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
            public CopyFileResult CopyFileAsyncResult;

            /// <inheritdoc />
            public Task<CopyFileResult> CopyFileAsync(PathBase path, AbsolutePath destinationPath, long contentSize, bool overwrite, CancellationToken cancellationToken)
                => Task.FromResult(CopyFileAsyncResult);

#pragma warning disable 649
            public CopyFileResult CopyToAsyncResult;
#pragma warning restore 649

            /// <inheritdoc />
            public Task<CopyFileResult> CopyToAsync(PathBase sourcePath, Stream destinationStream, long expectedContentSize, CancellationToken cancellationToken)
                => Task.FromResult(CopyToAsyncResult);

#pragma warning disable 649
            public FileExistenceResult CheckFileExistsAsyncResult;
#pragma warning restore 649

            /// <inheritdoc />
            public Task<FileExistenceResult> CheckFileExistsAsync(PathBase path, TimeSpan timeout, CancellationToken cancellationToken)
                => Task.FromResult(CheckFileExistsAsyncResult);
        }
    }
}
