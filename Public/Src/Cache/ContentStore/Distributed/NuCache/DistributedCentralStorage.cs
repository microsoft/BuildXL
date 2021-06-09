// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;
using ContentStore.Grpc;
#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// <see cref="CentralStorage"/> which uses uses distributed CAS as cache aside for a fallback central storage
    /// </summary>
    public class DistributedCentralStorage : CachingCentralStorage, IDistributedContentCopierHost
    {
        private readonly ILocationStore _locationStore;
        private readonly DistributedContentCopier _copier;
        private const string CacheSubFolderName = "dcs";
        private const string CacheSubFolderNameWithTrailingSlash = CacheSubFolderName + @"\";

        private const string CacheSharedSubFolderToReplace = @"Shared\" + CacheSubFolderName;
        private const string CacheSharedSubFolder = CacheSubFolderName + @"\Shared";

        private readonly ConcurrentDictionary<MachineLocation, MachineLocation> _machineLocationTranslationMap = new ConcurrentDictionary<MachineLocation, MachineLocation>();

        // Randomly generated seed for use when computing derived hash represent fake content for tracking
        // which machines have started copying a particular piece of content
        private const uint _startedCopyHashSeed = 1006063109;
        private readonly DisposableDirectory _copierWorkingDirectory;
        private int _translateLocationsOffset = 0;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(DistributedCentralStorage));

        /// <inheritdoc />
        protected override string PreprocessStorageId(string storageId) => storageId;

        /// <nodoc />
        public DistributedCentralStorage(
            DistributedCentralStoreConfiguration configuration,
            ILocationStore locationStore,
            DistributedContentCopier copier,
            CentralStorage fallbackStorage)
            : base(configuration, fallbackStorage, copier.FileSystem)
        {
            _copier = copier;
            _locationStore = locationStore;

            _copierWorkingDirectory = new DisposableDirectory(copier.FileSystem, PrivateCas!.RootPath / "Temp");
        }

        #region IDistributedContentCopierHost Members

        AbsolutePath IDistributedContentCopierHost.WorkingFolder => _copierWorkingDirectory.Path;

        void IDistributedContentCopierHost.ReportReputation(MachineLocation location, MachineReputation reputation)
        {
            // Don't report reputation as this component modifies machine locations so they won't be recognized
            // by the machine reputation tracker
        }

        #endregion IDistributedContentCopierHost Members

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _copierWorkingDirectory.Dispose();

            return base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<Result<ContentHashWithSize>> TryGetAndPutFileAsync(OperationContext context, string storageId, AbsolutePath targetFilePath, bool isImmutable)
        {
            var result = await base.TryGetAndPutFileAsync(context, storageId, targetFilePath, isImmutable);

            if (result.TryGetValue(out var contentHashWithSize))
            {
                // Register that the machine now has the content
                await RegisterContent(context, contentHashWithSize);
            }

            return result;
        }


        /// <summary>
        /// TODO: try to refactor this to use the same logic as ReadOnlyDistributedContentSession.
        /// </summary>
        private Task<PushFileResult> PushCheckpointFileAsync(OperationContext context, ContentHashWithSize hashWithSize)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var destinationMachineResult = _locationStore.ClusterState.GetRandomMachineLocation(Array.Empty<MachineLocation>());
                if (!destinationMachineResult.Succeeded)
                {
                    return new PushFileResult(destinationMachineResult, "Failed to get a location to proactively copy the checkpoint file.");
                }

                var destionationMachine = destinationMachineResult.Value;

                var streamResult = await PrivateCas.OpenStreamAsync(context, hashWithSize.Hash, pinRequest: null);
                if (!streamResult.Succeeded)
                {
                    return new PushFileResult(streamResult, "Should have been able to open the stream from the local CAS");
                }

                using var stream = streamResult.Stream!;
                return await _copier.PushFileAsync(
                    context,
                    hashWithSize,
                    destionationMachine,
                    stream,
                    isInsideRing: false,
                    CopyReason.ProactiveCheckpointCopy,
                    ProactiveCopyLocationSource.Random,
                    attempt: 0);
            },
            extraStartMessage: $"Hash=[{hashWithSize.Hash.ToShortString()}]",
            extraEndMessage: _ => $"Hash=[{hashWithSize.Hash.ToShortString()}]");
        }

        protected override async Task<bool> TryRetrieveFromExternalCacheAsync(OperationContext context, ContentHash hash)
        {
            var putResult = await CopyLocalAndPutAsync(context, hash);
            if (!putResult.Succeeded)
            {
                Tracer.Debug(context, $"Falling back to blob storage. Error={putResult}");
            }
            else
            {
                Counters[CentralStorageCounters.TryGetFileFromPeerSucceeded].Increment();
            }

            return putResult.Succeeded;
        }

        private Task<PutResult> CopyLocalAndPutAsync(OperationContext operationContext, ContentHash hash)
        {
            return operationContext.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var startedCopyHash = ComputeStartedCopyHash(hash);
                    await RegisterContent(context, new ContentHashWithSize(startedCopyHash, -1));

                    for (int i = 0; i < Configuration.PropagationIterations; i++)
                    {
                        // If initial place fails, try to copy the content from remote locations
                        var (hashInfo, pendingCopyCount) = await GetFileLocationsAsync(context, hash, startedCopyHash);

                        var machineId = _locationStore.ClusterState.PrimaryMachineId.Index;
                        int machineNumber = GetMachineNumber();
                        var requiredReplicas = ComputeRequiredReplicas(machineNumber);

                        var actualReplicas = hashInfo.Locations?.Count ?? 0;

                        // Copy from peers if:
                        // The number of pending copies is known to be less that the max allowed copies
                        // OR the number replicas exceeds the number of required replicas computed based on the machine index
                        bool shouldCopy = pendingCopyCount < Configuration.MaxSimultaneousCopies || actualReplicas >= requiredReplicas;

                        Tracer.Debug(context, $"{i} (ShouldCopy={shouldCopy}): Hash={hash.ToShortString()}, Id={machineId}" +
                            $", Replicas={actualReplicas}, RequiredReplicas={requiredReplicas}, Pending={pendingCopyCount}, Max={Configuration.MaxSimultaneousCopies}");

                        if (shouldCopy)
                        {
                            return await _copier.TryCopyAndPutAsync(
                                context,
                                new DistributedContentCopier.CopyRequest(
                                    this,
                                    hashInfo,
                                    CopyReason.CentralStorage,
                                    args => PrivateCas.PutFileAsync(context, args.tempLocation, FileRealizationMode.Move, hash, pinRequest: null),
                                    // Most of these transfers are large files (sst files), but they are also already
                                    // compressed, so compressing over it would only waste cycles.
                                    CopyCompression.None
                                    ));
                        }

                        // Wait for content to propagate to more machines
                        await Task.Delay(Configuration.PropagationDelay, context.Token);
                    }

                    return new PutResult(hash, "Insufficient replicas");
                },
                traceErrorsOnly: true,
                extraEndMessage: _ => $"ContentHash=[{hash}]",
                timeout: Configuration.PeerToPeerCopyTimeout);
        }


        protected override async Task<PutResult> PutFileAsync(OperationContext context, AbsolutePath file, ContentHash? hash, bool isImmutable = false, bool isUpload = false)
        {
            var putResult = await base.PutFileAsync(context, file, hash, isImmutable);
            if (putResult.Succeeded)
            {
                var contentInfo = new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize);
                await RegisterContent(context, contentInfo);

                if (isUpload && Configuration.ProactiveCopyCheckpointFiles)
                {
                    var hashWithSize = new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize);
                    var pushResult = await PushCheckpointFileAsync(context, hashWithSize)
                        .FireAndForgetOrInlineAsync(context, Configuration.InlineCheckpointProactiveCopies)
                        .ThrowIfFailureAsync();
                }
            }

            return putResult;
        }

        private async Task<(ContentHashWithSizeAndLocations info, int pendingCopies)> GetFileLocationsAsync(OperationContext context, ContentHash hash, ContentHash startedCopyHash)
        {
            // Locations are registered under the derived fake startedCopyHash to keep a count of which machines have started
            // copying content. This allows computing the amount of pending copies by subtracting the machines which have
            // finished copying (i.e. location is registered with real hash)
            var result = await _locationStore.GetBulkAsync(context, new[] { hash, startedCopyHash }).ThrowIfFailure();
            var info = result.ContentHashesInfo[0];

            var startedCopyLocations = result.ContentHashesInfo[1].Locations!;
            var finishedCopyLocations = info.Locations!;
            var pendingCopies = startedCopyLocations.Except(finishedCopyLocations).Count();

            var locations = TranslateLocations(info.Locations!);

            return (new ContentHashWithSizeAndLocations(info.ContentHash, info.Size, locations), pendingCopies);
        }

        private ContentHash ComputeStartedCopyHash(ContentHash hash)
        {
            var murmurHash = MurmurHash3.Create(hash.ToByteArray(), _startedCopyHashSeed);

            var hashLength = HashInfoLookup.Find(HashType).ByteLength;
            var buffer = murmurHash.ToByteArray();
            Array.Resize(ref buffer, hashLength);

            return new ContentHash(HashType, buffer);
        }

        private Task RegisterContent(OperationContext context, params ContentHashWithSize[] contentInfo)
        {
            return _locationStore.RegisterLocalLocationAsync(context, contentInfo).ThrowIfFailure();
        }

        internal IReadOnlyList<MachineLocation> TranslateLocations(IReadOnlyList<MachineLocation> locations)
        {
            // Choose a 'random' offset to ensure that locations are random
            // Locations are normally randomly sorted except machine reputation can override this
            // For content which is pulled on all machines like that in the central storage, it is more
            // important not to overload a machine which may end up consistent at the top of the list because of
            // having a good reputation
            var offset = Interlocked.Increment(ref _translateLocationsOffset);

            return locations.SelectList((item, index) => TranslateLocation(locations[getOffsetIndex(index, offset, locations.Count)]));

            static int getOffsetIndex(int index, int offset, int totalCount)
            {
                if (index == totalCount - 1)
                {
                    // It's important that the last entry remains at the end of the list, because most times that corresponds
                    // to the master, which we want to avoid copying from at all costs.
                    return index;
                }

                return (offset + index) % (totalCount - 1);
            }
        }

        private MachineLocation TranslateLocation(MachineLocation other)
        {
            if (_machineLocationTranslationMap.TryGetValue(other, out var translated))
            {
                return translated;
            }

            var otherPath = other.Path;

            bool hasTrailingSlash = otherPath.EndsWith(@"\");

            // Add dcs subfolder to the path
            otherPath = Path.Combine(otherPath, hasTrailingSlash ? CacheSubFolderNameWithTrailingSlash : CacheSubFolderName);

            // If other already ended with shared, this will rearrange so that the shared folder is under the dcs sub folder
            otherPath = otherPath.ReplaceIgnoreCase(CacheSharedSubFolderToReplace, CacheSharedSubFolder);

            var location = new MachineLocation(otherPath);
            _machineLocationTranslationMap[other] = location;
            return location;
        }

        /// <summary>
        /// Computes an index for the machine among active machines
        /// </summary>
        private int GetMachineNumber()
        {
            var machineId = _locationStore.ClusterState.PrimaryMachineId.Index;
            var machineNumber = machineId - _locationStore.ClusterState.InactiveMachines.Where(id => id.Index < machineId).Count();
            return machineNumber;
        }

        private int ComputeRequiredReplicas(int index)
        {
            if (index <= 0)
            {
                return 1;
            }

            // Threshold is index / MaxSimultaneousCopies.
            // This ensures when locations are chosen at random there should be on average MaxSimultaneousCopies or less
            // from the set of locations assuming worst case where all machines are trying to copy concurrently
            var machineThreshold = index / Configuration.MaxSimultaneousCopies;
            return Math.Max(1, machineThreshold);
        }

        /// <summary>
        /// Defines content location store functionality needed for <see cref="DistributedCentralStorage"/>
        /// </summary>
        public interface ILocationStore
        {
            /// <summary>
            /// The cluster state
            /// </summary>
            ClusterState ClusterState { get; }

            /// <summary>
            /// Gets content locations for content
            /// </summary>
            Task<GetBulkLocationsResult> GetBulkAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes);

            /// <summary>
            /// Registers content location for current machine
            /// </summary>
            Task<BoolResult> RegisterLocalLocationAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentInfo);
        }
    }
}
