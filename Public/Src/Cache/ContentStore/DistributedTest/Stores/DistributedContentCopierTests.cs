// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStore.Grpc;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Stores
{
    using ContentLocation = BuildXL.Cache.ContentStore.Distributed.ContentLocation;

    public class DistributedContentCopierTests : TestBase
    {
        public DistributedContentCopierTests(ITestOutputHelper output)
            : base(() => new MemoryFileSystem(TestSystemClock.Instance), TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task CopyFromInRingMachines()
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var (distributedCopier, mockFileCopier) = CreateMocks(FileSystem, directory.Path, TimeSpan.Zero);
                await using var _ = await distributedCopier.StartupWithAutoShutdownAsync(context);

                var hash = VsoHashInfo.Instance.EmptyHash;
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 42,
                    new MachineLocation[0]);

                mockFileCopier.CopyToAsyncResult = CopyFileResult.SuccessWithSize(42);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 42)),
                    inRingMachines: new MachineLocation[] { new MachineLocation("") });

                result.ShouldBeSuccess();
            }
        }

        [Fact]
        public async Task CopyFailsWithNoLocations()
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var (distributedCopier, mockFileCopier) = CreateMocks(FileSystem, directory.Path, TimeSpan.Zero);
                await using var _ = await distributedCopier.StartupWithAutoShutdownAsync(context);

                var hash = VsoHashInfo.Instance.EmptyHash;
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 42,
                    new MachineLocation[0]);

                mockFileCopier.CopyToAsyncResult = CopyFileResult.SuccessWithSize(42);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 42)));

                result.ShouldBeError();
            }
        }

        [Fact]
        public async Task CopyFailsForWrongCopySize()
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var (distributedCopier, mockFileCopier) = CreateMocks(FileSystem, directory.Path, TimeSpan.Zero);
                await using var _ = await distributedCopier.StartupWithAutoShutdownAsync(context);

                var hash = VsoHashInfo.Instance.EmptyHash;
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 42,
                    new MachineLocation[] {new MachineLocation("")});

                mockFileCopier.CopyToAsyncResult = CopyFileResult.SuccessWithSize(41);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 42)));

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
                var (distributedCopier, mockFileCopier) = CreateMocks(FileSystem, directory.Path, TimeSpan.Zero);
                await using var _ = await distributedCopier.StartupWithAutoShutdownAsync(context);

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
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(wrongHash, 42)));

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
                var (distributedCopier, mockFileCopier) = CreateMocks(FileSystem, directory.Path,TimeSpan.Zero, retries);
                await using var _ = await distributedCopier.StartupWithAutoShutdownAsync(context);

                var machineLocations = new MachineLocation[] {new MachineLocation("")};

                var hash = ContentHash.Random();
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 99,
                    machineLocations);

                mockFileCopier.CopyToAsyncResult = CopyFileResult.FromResultCode(CopyResultCode.UnknownServerError);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 99)));
                result.ShouldBeError();
                mockFileCopier.CopyAttempts.Should().Be(retries);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        public async Task CopyRetriesWithRestrictions(int retries)
        {
            var context = new Context(Logger);
            var copyAttemptsWithRestrictedReplicas = 2;
            var restrictedCopyReplicaCount = 3;
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var (distributedCopier, mockFileCopier) = CreateMocks(
                    FileSystem,
                    directory.Path,
                    TimeSpan.Zero,
                    retries,
                    copyAttemptsWithRestrictedReplicas,
                    restrictedCopyReplicaCount,
                    maxRetryCount: retries + 1);
                await using var _ = await distributedCopier.StartupWithAutoShutdownAsync(context);

                var machineLocations = new MachineLocation[] { new MachineLocation(""), new MachineLocation(""), new MachineLocation(""), new MachineLocation(""), new MachineLocation("") };

                var hash = ContentHash.Random();
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 99,
                    machineLocations);

                mockFileCopier.CopyToAsyncResult = CopyFileResult.FromResultCode(CopyResultCode.UnknownServerError);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 99)));

                result.ShouldBeError();
                int copyAttempts = 0;
                for (var attemptCount = 0; attemptCount < retries; attemptCount++)
                {
                    var maxReplicaCount = attemptCount < copyAttemptsWithRestrictedReplicas
                        ? restrictedCopyReplicaCount
                        : int.MaxValue;

                    copyAttempts += Math.Min(maxReplicaCount, machineLocations.Length);
                }

                if (copyAttempts < distributedCopier.Settings.MaxRetryCount)
                {
                    mockFileCopier.CopyAttempts.Should().Be(copyAttempts);
                    result.ErrorMessage.Should().NotContain("Maximum total retries");
                }
                else
                {
                    mockFileCopier.CopyAttempts.Should().Be(distributedCopier.Settings.MaxRetryCount);
                    result.ErrorMessage.Should().Contain("Maximum total retries");
                }
            }
        }

        ///<summary>
        /// Test case for bug https://dev.azure.com/mseng/1ES/_boards/board/t/DavidW%20-%20Team/Stories/?workitem=1654106
        /// During the first attempt of copying from a list of locations, one of the locations returns a DestinationPathError.
        /// Then in subsequent attempts to copy from the list of locations, the previous location that returned DestinationPathError now returns a different error.
        /// We should still be able to attempt to copy and return without and out of range exception thrown.
        ///</summary>
        [Theory]
        [InlineData(3)]
        public async Task CopyWithDestinationPathError(int retries)
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var (distributedCopier, mockFileCopier) = CreateMocks(FileSystem, directory.Path, TimeSpan.FromMilliseconds((10)), retries);
                await using var _ = await distributedCopier.StartupWithAutoShutdownAsync(context);

                var machineLocations = new MachineLocation[] { new MachineLocation(""), new MachineLocation("") };

                var hash = ContentHash.Random();
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 99,
                    machineLocations);
                mockFileCopier.CopyAttempts = 0;
                var totalCopyAttempts = (retries - 1) * machineLocations.Length + 1;
                mockFileCopier.CustomResults = new CopyFileResult[totalCopyAttempts];
                mockFileCopier.CustomResults[0] = CopyFileResult.FromResultCode(CopyResultCode.DestinationPathError);
                for(int counter = 1; counter < totalCopyAttempts; counter ++)
                {
                    mockFileCopier.CustomResults[counter] = CopyFileResult.FromResultCode(CopyResultCode.UnknownServerError);
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

        public static (TestDistributedContentCopier, MockFileCopier) CreateMocks(
            IAbsFileSystem fileSystem,
            AbsolutePath rootDirectory,
            TimeSpan retryInterval,
            int retries = 1,
            int copyAttemptsWithRestrictedReplicas = 0,
            int restrictedCopyReplicaCount = 3,
            int maxRetryCount = 32)
        {
            var mockFileCopier = new MockFileCopier();
            var contentCopier = new TestDistributedContentCopier(
                rootDirectory,
                // Need to use exactly one retry.
                new DistributedContentStoreSettings()
                {
                    RetryIntervalForCopies = Enumerable.Range(0, retries).Select(r => retryInterval).ToArray(),
                    CopyAttemptsWithRestrictedReplicas = copyAttemptsWithRestrictedReplicas,
                    RestrictedCopyReplicaCount = restrictedCopyReplicaCount,
                    TrustedHashFileSizeBoundary = long.MaxValue, // Disable trusted hash because we never actually move bytes and thus the hasher thinks there is a mismatch.
                    MaxRetryCount = maxRetryCount,
                },
                fileSystem,
                mockFileCopier,
                copyRequester: null);
            return (contentCopier, mockFileCopier);
        }

        public class MockFileCopier : IRemoteFileCopier
        {
            public int CopyAttempts = 0;
#pragma warning disable 649
            public CopyFileResult CopyToAsyncResult = CopyFileResult.Success;
#pragma warning restore 649
            public CopyFileResult[] CustomResults;

            public MachineLocation GetLocalMachineLocation(AbsolutePath cacheRoot) => new MachineLocation("");

            /// <inheritdoc />
            public Task<CopyFileResult> CopyToAsync(OperationContext context, ContentLocation sourceLocation, Stream destinationStream, CopyOptions options)
            {
                CopyAttempts++;
                if (CustomResults != null)
                {
                    return Task.FromResult(CustomResults[CopyAttempts - 1]);
                }
                return Task.FromResult(CopyToAsyncResult);
            }
        }
    }

    public class TestDistributedContentCopier : DistributedContentCopier, IDistributedContentCopierHost
    {
        public TestDistributedContentCopier(
            AbsolutePath workingDirectory,
            DistributedContentStoreSettings settings,
            IAbsFileSystem fileSystem,
            IRemoteFileCopier fileCopier,
            IContentCommunicationManager copyRequester)
            : base(settings, fileSystem, fileCopier, copyRequester, TestSystemClock.Instance, TestGlobal.Logger)
        {
            Settings = settings;
            WorkingFolder = workingDirectory;
        }

        public DistributedContentStoreSettings Settings { get; }

        public AbsolutePath WorkingFolder { get; }

        public void ReportReputation(MachineLocation location, MachineReputation reputation)
        {
        }

        protected override async Task<CopyFileResult> CopyFileAsync(
            OperationContext context,
            IRemoteFileCopier copier,
            ContentLocation sourcePath,
            AbsolutePath destinationPath,
            long expectedContentSize,
            bool overwrite,
            CopyOptions options,
            CancellationToken cancellationToken)
        {
            // TODO: why the destination str
            using var destinationStream = FileSystem.OpenForWrite(destinationPath, sourcePath.Size, FileMode.Create, FileShare.None, FileOptions.None, 1024);
            return await copier.CopyToAsync(context, sourcePath, destinationStream, options);
        }

        internal Task<PutResult> TryCopyAndPutAsync(
            OperationContext operationContext,
            ContentHashWithSizeAndLocations hashWithLocations,
            Func<(CopyFileResult copyResult, AbsolutePath tempLocation, int attemptCount), Task<PutResult>> handleCopyAsync,
            IReadOnlyList<MachineLocation> inRingMachines = null)
        {
            return TryCopyAndPutAsync(operationContext, new CopyRequest(this, hashWithLocations, CopyReason.None, handleCopyAsync, CopyCompression.None, inRingMachines));
        }
    }
}
