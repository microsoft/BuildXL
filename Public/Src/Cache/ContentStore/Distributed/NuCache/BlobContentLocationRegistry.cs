// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using static BuildXL.Cache.ContentStore.Distributed.NuCache.BlobFolderStorage;
using static BuildXL.Cache.ContentStore.Utils.DateTimeUtilities;
using EnumerableExtensions = BuildXL.Cache.ContentStore.Extensions.EnumerableExtensions;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable annotations

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public record BlobContentLocationRegistryConfiguration : BlobContentLocationRegistrySettings, IBlobFolderStorageConfiguration
    {
        public AzureBlobStorageCredentials? Credentials { get; set; }

        public RetryPolicyConfiguration RetryPolicy { get; set; } = BlobFolderStorage.DefaultRetryPolicy;

        TimeSpan IBlobFolderStorageConfiguration.StorageInteractionTimeout => StorageInteractionTimeout;

        public BlobContentLocationRegistryConfiguration(BlobContentLocationRegistrySettings settings)
            : base(settings)
        {
        }

        public BlobContentLocationRegistryConfiguration()
        {
        }
    }

    /// <summary>
    /// Tracking content locations using blobs in Azure storage.
    ///
    /// Partition submission blob - contains listing of all machine content in the partition (one block per machine)
    /// Partition output blob - contains blocks for each processed output based on the partition submission blob.
    ///     FullListing - full sorted listing of content for all machines. (Basically just the sorted partition input sorted by hash, then machine).
    ///     DiffListing - listing of adds/removes from base snapshot of FullListing blob
    ///     FullSst - same information as FullListing but stored as entries in RocksDb sst file (not yet implemented)
    ///     DiffSst - same information as DiffListing but stored as entries in RocksDb sst file (not yet implemented)
    /// 
    /// Content is partitioned based on first byte of hash into 256 partitions.
    /// Machines push their content for a partition as a block in the partition input blob (this can be done in parallel).
    /// </summary>
    public class BlobContentLocationRegistry : StartupShutdownComponentBase
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobContentLocationRegistry));

        /// <summary>
        /// Internal use only. Test hook.
        /// </summary>
        internal TestObserver Observer { get; set; } = new();

        internal readonly BlobContentLocationRegistryConfiguration Configuration;
        private ILocalContentStore? _localContentStore;
        private readonly ClusterStateManager _clusterStateManager;
        private readonly IClock _clock;

        internal readonly BlobFolderStorage Storage;

        private ClusterState ClusterState => _clusterStateManager.ClusterState;
        private MachineRecord LocalMachineRecord { get; set; }

        /// <summary>
        /// Retry policy which retries PutBlock operations which fail because the lease is taken.
        /// Lease is only taken for short periods of time to commit the block list
        /// </summary>
        private readonly IRetryPolicy _putBlockRetryPolicy;

        public BlobContentLocationRegistry(
            BlobContentLocationRegistryConfiguration configuration,
            ClusterStateManager clusterStateManager,
            ILocalContentStore? localContentStore,
            MachineLocation primaryMachineLocation,
            IClock? clock = null)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            Configuration = configuration;
            _localContentStore = localContentStore;
            _clusterStateManager = clusterStateManager;
            _clock = clock ?? SystemClock.Instance;

            Storage = new BlobFolderStorage(Tracer, configuration);

            LinkLifetime(Storage);
            LinkLifetime(clusterStateManager);

            _putBlockRetryPolicy = RetryPolicyFactory.GetExponentialPolicy(shouldRetry: ex => IsPreconditionFailedError(ex));

            if (configuration.UpdateInBackground)
            {
                RunInBackground(nameof(UpdatePartitionsLoopAsync), UpdatePartitionsLoopAsync, fireAndForget: true);
            }
        }

        private static bool IsPreconditionFailedError(Exception ex)
        {
            return ex is StorageException storageException && storageException.RequestInformation.HttpStatusCode == (int)HttpStatusCode.PreconditionFailed;
        }

        internal void SetLocalContentStore(ILocalContentStore localContentStore)
        {
            Contract.Requires(localContentStore != null);
            _localContentStore = localContentStore;
        }

        /// <summary>
        /// Background loop which submitted machine content and processes partition content blobs
        /// </summary>
        private async Task<BoolResult> UpdatePartitionsLoopAsync(OperationContext context)
        {
            while (!context.Token.IsCancellationRequested)
            {
                await UpdatePartitionsAsync(context).IgnoreFailure();

                await _clock.Delay(Configuration.PartitionsUpdateInterval, context.Token);
            }

            return BoolResult.Success;
        }

        internal Task<BoolResult> UpdatePartitionsAsync(OperationContext context, Func<byte, bool>? excludePartition = null)
        {
            LocalMachineRecord = _clusterStateManager.ClusterState.PrimaryMachineRecord;
            if (LocalMachineRecord == null || _localContentStore == null)
            {
                return BoolResult.SuccessTask;
            }

            context = context.CreateNested(Tracer.Name);
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var sortedEntries = await GetLocalSortedEntriesAsync(context);
                    var partitions = sortedEntries.GetPartitionSlices().ToList();

                    // Randomly enumerate partitions so machines do not all operation on the same partition concurrently
                    foreach (var partitionId in EnumerableExtensions.PseudoRandomEnumerate(partitions.Count).Select(p => (byte)p))
                    {
                        if (excludePartition?.Invoke(partitionId) == true)
                        {
                            continue;
                        }

                        var partition = partitions[partitionId];

                        // Add a block with the machine's content to the corresponding partition submission block
                        await SubmitMachinePartitionContentAsync(context, partitionId, partition);

                        // Attempt to process the submission block for the partition into a new partition output blob
                        await ProcessPartitionAsync(context, partitionId);

                        // Add a delay to not have a hot loop of storage interaction
                        await Task.Delay(Configuration.PerPartitionDelayInterval, context.Token);
                    }

                    return BoolResult.Success;
                });
        }

        private Task<T> ExecuteWithRetryAsync<T>(
            OperationContext context,
            BlobName blobName,
            Func<Task<T>> executeAsync,
            [CallerMemberName] string operation = null)
        {
            bool traceErrorAndReturnFalse(StorageException ex)
            {
                if (IsPreconditionFailedError(ex))
                {
                    context.TracingContext.Debug($"Precondition failed. Data=[Blob={blobName}] Exception=[{ex.Message}]", Tracer.Name, operation);
                }

                return false;
            }

            return _putBlockRetryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    return await executeAsync();
                }
                catch (StorageException ex) when (traceErrorAndReturnFalse(ex))
                {
                    throw;
                }
            },
            context.Token);
        }

        /// <summary>
        /// Submit the machine's content for the given partition to the submission blob as a blob.
        /// </summary>
        private Task SubmitMachinePartitionContentAsync(
            OperationContext context,
            byte partitionId,
            ContentListing partition)
        {
            Observer.OnPutBlock(partitionId, partition);

            return Storage.UseBlockBlobAsync<BoolResult>(context,
                GetPartitionSubmissionBlobName(partitionId),
                (context, blob) =>
                {
                    var hash = ContentHashingHelper.CalculateHash(partition.Bytes, HashType.MD5);

                    return ExecuteWithRetryAsync(context, blob.Name, async () =>
                    {
                        using var stream = partition.AsStream();
                        await blob.PutBlockAsync(
                            LocalMachineRecord.MachineBlockId,
                            stream,
                            md5Hash: hash);
                        return BoolResult.Success;
                    });
                },
                caller: "WritePartitionBlock",
                endMessageSuffix: _ => $"Partition={partitionId}, BlockId={LocalMachineRecord.MachineBlockId}, {partition}")
                .IgnoreFailure();
        }

        private record struct PartitionComputationResult(ContentListing Listing, int BlockCount, int RetainedBlockCount);

        /// <summary>
        /// Computes the sorted partition content from the submission blob
        /// </summary>
        public Task<ContentListing> ComputeSortedPartitionContentAsync(
            OperationContext context,
            byte partitionId,
            bool takeLease = true)
        {
            return Storage.UseBlockBlobAsync<Result<PartitionComputationResult>>(context,
                GetPartitionSubmissionBlobName(partitionId),
                timeout: TimeSpan.FromSeconds(120),
                useAsync: async (context, blob) =>
                {
                    AccessCondition leaseCondition = null;
                    if (takeLease)
                    {
                        var leaseId = await blob.AcquireLeaseAsync(TimeSpan.FromSeconds(60));
                        leaseCondition = AccessCondition.GenerateLeaseCondition(leaseId);
                    }

                    var blockList = await blob.DownloadBlockListAsync(
                            BlockListingFilter.All,
                            leaseCondition);

                    var distinctBlocks = blockList.Select(b => b.Name).ToHashSet();
                    var retainedBlockList = distinctBlocks
                        .Intersect(ClusterState.LiveRecords.Select(e => e.MachineBlockId))
                        .ToList();

                    await blob.PutBlockListAsync(
                        retainedBlockList,
                        leaseCondition);

                    var listing = await ReadContentPartitionAsync(context, partitionId, blob);

                    if (leaseCondition != null)
                    {
                        await blob.ReleaseLeaseAsync(leaseCondition);
                    }

                    var result = new PartitionComputationResult(
                        listing,
                        BlockCount: distinctBlocks.Count,
                        RetainedBlockCount: retainedBlockList.Count);

                    result.Listing.SortAndDeduplicate();

                    return Result.Success(result);
                },
                endMessageSuffix: r => $" Partition={partitionId}, Value={r.GetValueOrDefault()}")
                .AsAsync(r => r.Listing)
                .ThrowIfFailureAsync(unwrap: true);
        }

        /// <summary>
        /// Reads the content listing from the blob block
        /// </summary>
        internal Task<ContentListing> ReadContentPartitionAsync(
            OperationContext context,
            byte partitionId,
            BlobName name,
            PartitionOutputKind kind,
            AccessCondition accessCondition = null)
        {
            return Storage.UseBlockBlobAsync(
                context,
                name,
                async (context, blob) =>
                {
                    bool exists = await blob.ExistsAsync();
                    if (!exists)
                    {
                        return ContentListing.CreateFromByteLength(0);
                    }

                    var blockList = await blob.DownloadBlockListAsync(
                        BlockListingFilter.Committed,
                        accessCondition);

                    var blockId = GetPartitionOutputBlockId(kind);

                    // Find the range for the block id
                    BlobRange range = new BlobRange() { Length = -1 };
                    foreach (var block in blockList)
                    {
                        if (block.Name == blockId)
                        {
                            range.Length = block.Length;
                            break;
                        }
                        else
                        {
                            range.Offset += block.Length;
                        }
                    }

                    if (range.Length < 0)
                    {
                        return new ErrorResult($"Block '{blockId}' not found in blob: Existing blocks: {string.Join(", ", blockList.Select(b => b.Name))}");
                    }

                    var result = await ReadContentPartitionAsync(context, partitionId, blob, range, accessCondition);
                    return Result.Success(result);
                }).ThrowIfFailureAsync(unwrap: true);
        }

        /// <summary>
        /// Reads the content listing from the blob (with optional range)
        /// </summary>
        private Task<ContentListing> ReadContentPartitionAsync(
            OperationContext context,
            byte partitionId,
            BlobWrapper blob,
            BlobRange? range = null,
            AccessCondition accessCondition = null)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    await blob.FetchAttributesAsync();
                    var listing = ContentListing.CreateFromByteLength((int)(range?.Length ?? blob.Blob.Properties.Length));
                    bool disposeOnException()
                    {
                        listing.Dispose();
                        return false;
                    }
                    try
                    {
                        using var stream = listing.AsStream();
                        await blob.DownloadRangeToStreamAsync(
                            stream,
                            range?.Offset,
                            range?.Length,
                            accessCondition);
                        return Result.Success(listing);
                    }
                    catch when (disposeOnException())
                    {
                        throw;
                    }
                },
                extraEndMessage: r => $"Partition={partitionId}, Blob={blob.Name}, Value={r.GetValueOrDefault()}")
                .ThrowIfFailureAsync(unwrap: true);
        }

        /// <summary>
        /// Processes the partition submission blob against the current base snapshot to create a new output blob.
        /// </summary>
        public Task ProcessPartitionAsync(
            OperationContext context,
            byte partitionId)
        {
            return Storage.UseBlockBlobAsync<Result<UpdatePartitionResult>>(context,
                GetPartitionOutputBlobName(partitionId),
                caller: "WritePartitionBlobs",
                timeout: TimeSpan.FromSeconds(120),
                useAsync: async (context, blob) =>
                {
                    var metadata = new BlobMetadata(blob);
                    var updateResult = new UpdatePartitionResult(new PartitionChangeCounters(), metadata, Updated: false);

                    if (!await blob.ExistsAsync())
                    {
                        await blob.UploadFromByteArrayAsync(
                            new ArraySegment<byte>(Array.Empty<byte>(), 0, 0),
                            AccessCondition.GenerateIfNotExistsCondition());
                    }

                    if (metadata.LastUpdateTime is DateTime lastUpdateTime
                        && lastUpdateTime.IsRecent(_clock.UtcNow, Configuration.PartitionsUpdateInterval))
                    {
                        return Result.Success(updateResult);
                    }

                    updateResult.Updated = true;

                    // Acquire lease for maximum non-infinite duration.
                    var leaseId = await blob.AcquireLeaseAsync(TimeSpan.FromSeconds(60));

                    var manifest = await Storage
                        .ReadAsync<PartitionCheckpointManifest>(context, Configuration.PartitionCheckpointManifestFileName)
                        .ThrowIfFailureAsync();

                    var baseRecord = manifest.Records[partitionId];
                    AccessCondition accessCondition = AccessCondition.GenerateLeaseCondition(leaseId);

                    // Get base listing
                    using var baseContentListing = baseRecord.SnapshotTime == null
                        ? ContentListing.CreateFromByteLength(0)
                        : await ReadContentPartitionAsync(
                            context,
                            partitionId,
                            GetPartitionOutputBlobName(partitionId, baseRecord.SnapshotTime),
                            PartitionOutputKind.FullListing,
                            accessCondition);

                    using var nextContentListing = await ComputeSortedPartitionContentAsync(context, partitionId);

                    var nextEntries = nextContentListing.EnumerateEntries();

                    var blockList = new List<string>();

                    // Compute the differences (use LastOrDefault to force enumeration of entire enumerable)
                    baseContentListing.EnumerateChanges(nextEntries, updateResult.UpdateState).LastOrDefault();

                    // Write the full listing blob
                    await PutPartitionOutputBlockAsync(
                        context,
                        partitionId,
                        blob,
                        PartitionOutputKind.FullListing,
                        blockList,
                        () => nextContentListing.AsStream(),
                        accessCondition);

                    // Finalize the blob
                    // Update metadata which will be associated with the block during PutBlockList
                    metadata.BaseSnapshot = baseRecord.SnapshotTime;
                    metadata.LastUpdateTime = _clock.UtcNow;

                    await blob.PutBlockListAsync(
                        blockList,
                        accessCondition);

                    await blob.ReleaseLeaseAsync(accessCondition);

                    return Result.Success(updateResult);
                },
                endMessageSuffix: r => $" Partition={partitionId}, UpdateResult={r.GetValueOrDefault()}")
                .IgnoreFailure();
        }

        /// <summary>
        /// Puts a partition output block
        /// </summary>
        private Task PutPartitionOutputBlockAsync(
            OperationContext context,
            byte partitionId,
            BlobWrapper blob,
            PartitionOutputKind kind,
            List<string> blockList,
            Func<Stream> getStream,
            AccessCondition accessCondition)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    using (var stream = getStream())
                    {
                        var blockId = GetPartitionOutputBlockId(kind);

                        await blob.PutBlockAsync(
                            blockId,
                            stream,
                            accessCondition);

                        blockList.Add(blockId);

                        return BoolResult.Success;
                    }
                },
                caller: $"WritePartition{kind}Block",
                extraEndMessage: _ => $"Partition={partitionId}")
                .ThrowIfFailureAsync();
        }

        private static BlobName GetPartitionSubmissionBlobName(byte partitionId)
        {
            var hexPartitionId = HexUtilities.BytesToHex(new[] { partitionId });
            return $"{hexPartitionId[0]}/{hexPartitionId}.bin";
        }

        private static BlobName GetPartitionOutputBlobName(byte partitionId, DateTimeOffset? snapshotTime = null)
        {
            var hexPartitionId = HexUtilities.BytesToHex(new[] { partitionId });
            BlobName blobName = $"{hexPartitionId[0]}/{hexPartitionId}.out.bin";
            blobName = blobName with { SnapshotTime = snapshotTime };
            return blobName;
        }

        /// <summary>
        /// Gets the output block id for the given output kind
        /// </summary>
        private static string GetPartitionOutputBlockId(PartitionOutputKind kind)
        {
            return kind.ToString().PadRight(32, '0');
        }

        private Task<ContentListing> GetLocalSortedEntriesAsync(OperationContext context)
        {
            return context.PerformNonResultOperationAsync(
                Tracer,
                async () =>
                {
                    var localEntry = LocalMachineRecord;
                    var contentInfo = (await _localContentStore.GetContentInfoAsync(context.Token)).ToList();
                    var listing = new ContentListing(contentInfo, machineId: localEntry.Id);

                    // This routine relies on the sorting behavior that hashes are sorted by bytes 0 to n, then by hash type
                    listing.SortAndDeduplicate();
                    return listing;
                });
        }

        /// <summary>
        /// Manifest tracking the snapshots used for each partition in a checkpoint.
        /// </summary>
        private record PartitionCheckpointManifest
        {
            public PartitionRecord[] Records { get; } = new PartitionRecord[ContentListing.PartitionCount];
        }

        /// <summary>
        /// Record of snapshot of a partition used for the checkpoint (blob storage uses this a the snapshot id)
        /// </summary>
        private record struct PartitionRecord(DateTimeOffset? SnapshotTime);

        private record struct BlobRange(long Offset, long Length);

        private record struct UpdatePartitionResult(PartitionChangeCounters UpdateState, BlobMetadata Metadata, bool Updated);

        /// <summary>
        /// Wrapper for accessing typed values of blob metadata.
        /// 
        /// DO NOT RENAME properties as these are used as keys in metadata dictionary.
        /// </summary>
        private record struct BlobMetadata(BlobWrapper Blob)
        {
            /// <summary>
            /// The base snapshot used when computing the difference blocks.
            /// </summary>
            public DateTimeOffset? BaseSnapshot
            {
                get => Blob.GetMetadataOrDefault(s => DateTimeOffset.Parse(s, null, DateTimeStyles.RoundtripKind));
                set => Blob.SetMetadata(value?.ToString("O"));
            }

            /// <summary>
            /// Time of last update. Using a specific metadata value to allow for tests notion of time.
            /// </summary>
            public DateTime? LastUpdateTime
            {
                get => Blob.GetMetadataOrDefault(s => DateTime.Parse(s, null, DateTimeStyles.RoundtripKind));
                set => Blob.SetMetadata(value?.ToString("O"));
            }
        }

        internal enum PartitionOutputKind
        {
            Unset,

            /// <summary>
            /// SST file containing entries for all content in the partition
            /// </summary>
            FullSst,

            /// <summary>
            /// SST file containing entries for added/removed content in the partition since last iteration
            /// </summary>
            DiffSst,

            /// <summary>
            /// Content listing file containing entries for all content in the partition
            /// </summary>
            FullListing,

            /// <summary>
            /// Content listing file containing entries for added/removed content in the partition since last iteration
            /// </summary>
            DiffListing
        }

        /// <summary>
        /// Test hook.
        /// </summary>
        internal class TestObserver
        {
            internal virtual void OnPutBlock(byte partitionId, ContentListing partition)
            {
            }
        }
    }
}
