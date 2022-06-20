// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Serialization;
using BuildXL.Utilities.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using RocksDbSharp;
using static BuildXL.Cache.ContentStore.Distributed.NuCache.BlobFolderStorage;
using static BuildXL.Cache.ContentStore.Utils.DateTimeUtilities;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using EnumerableExtensions = BuildXL.Cache.ContentStore.Extensions.EnumerableExtensions;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable annotations

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public record BlobContentLocationRegistryConfiguration : BlobContentLocationRegistrySettings, IBlobFolderStorageConfiguration
    {
        [JsonIgnore]
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
    ///     FullSst - same information as FullListing but stored as entries in RocksDb sst file
    ///     DiffSst - listing of adds/removes from prior snapshot's content as RocksDb merge operators
    /// 
    /// Content is partitioned based on first byte of hash into 256 partitions.
    /// Machines push their content for a partition as a block in the partition input blob (this can be done in parallel).
    /// </summary>
    public partial class BlobContentLocationRegistry : StartupShutdownComponentBase
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

        private RocksDbContentMetadataDatabase Database { get; set; }

        private readonly ReadOnlyArray<PartitionId> _partitionIds;

        private readonly IAbsFileSystem _fileSystem = PassThroughFileSystem.Default;

        /// <summary>
        /// Gets when the master lease will expire for the current machine. Updates to database are only
        /// allowed when the master lease is valid.
        /// </summary>
        private DateTime _databaseUpdateLeaseExpiry = DateTime.MinValue;

        /// <summary>
        /// Action queue for executing concurrent storage operations
        /// Namely used for download sst files in parallel.
        /// </summary>
        private readonly ActionQueue _actionQueue;

        /// <summary>
        /// Retry policy which retries PutBlock operations which fail because the lease is taken.
        /// Lease is only taken for short periods of time to commit the block list
        /// </summary>
        private readonly IRetryPolicy _putBlockRetryPolicy;

        private bool ShouldUpdateDatabase => Configuration.UpdateDatabase && _databaseUpdateLeaseExpiry > _clock.UtcNow;

        public BlobContentLocationRegistry(
            BlobContentLocationRegistryConfiguration configuration,
            ClusterStateManager clusterStateManager,
            MachineLocation primaryMachineLocation,
            RocksDbContentMetadataDatabase database,
            ILocalContentStore? localContentStore = null,
            IClock? clock = null)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            Configuration = configuration;
            _localContentStore = localContentStore;
            _clusterStateManager = clusterStateManager;
            _clock = clock ?? SystemClock.Instance;
            Database = database;
            Storage = new BlobFolderStorage(Tracer, configuration);
            _partitionIds = PartitionId.GetPartitions(configuration.PartitionCount);

            if (localContentStore != null)
            {
                SetLocalContentStore(localContentStore);
            }

            LinkLifetime(Storage);
            LinkLifetime(database);
            LinkLifetime(clusterStateManager);

            _putBlockRetryPolicy = RetryPolicyFactory.GetExponentialPolicy(shouldRetry: ex => IsPreconditionFailedError(ex));
            _actionQueue = new ActionQueue(Configuration.MaxDegreeOfParallelism, useChannel: true);

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
            Contract.Requires(!StartupStarted);
            _localContentStore = localContentStore;
            LinkLifetime(localContentStore as IStartupShutdownSlim);
        }

        internal void SetDatabaseUpdateLeaseExpiry(DateTime? expiry)
        {
            _databaseUpdateLeaseExpiry = expiry ?? DateTime.MinValue;
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

        internal Task<BoolResult> UpdatePartitionsAsync(OperationContext context, Func<PartitionId, bool>? excludePartition = null)
        {
            LocalMachineRecord = _clusterStateManager.ClusterState.PrimaryMachineRecord;
            if (LocalMachineRecord == null || _localContentStore == null)
            {
                return BoolResult.SuccessTask;
            }

            int databaseUpdates = 0;
            bool updatedDatabaseManifest = false;

            context = context.CreateNested(Tracer.Name);
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var sortedEntries = await GetLocalSortedEntriesAsync(context);
                    var partitions = sortedEntries.GetPartitionSlices(_partitionIds).ToList();

                    // Randomly enumerate partitions so machines do not all operation on the same partition concurrently
                    foreach (var rangeIndex in EnumerableExtensions.PseudoRandomEnumerateRange(_partitionIds.Length))
                    {
                        var partitionId = _partitionIds[rangeIndex];
                        if (excludePartition?.Invoke(partitionId) == true)
                        {
                            continue;
                        }

                        var partition = partitions[rangeIndex];

                        // Add a block with the machine's content to the corresponding partition submission block
                        await SubmitMachinePartitionContentAsync(context, partitionId, partition);

                        if (Configuration.ProcessPartitions)
                        {
                            // Attempt to process the submission block for the partition into a new partition output blob
                            await ProcessPartitionAsync(context, partitionId);
                        }

                        if (ShouldUpdateDatabase)
                        {
                            databaseUpdates++;
                            await UpdateDatabasePartitionAsync(context, partitionId);
                        }

                        // Add a delay to not have a hot loop of storage interaction
                        await Task.Delay(Configuration.PerPartitionDelayInterval, context.Token);
                    }

                    if (ShouldUpdateDatabase)
                    {
                        updatedDatabaseManifest = true;
                        await UpdateManifestFromDatabaseRecordAsync(context).ThrowIfFailureAsync();
                    }

                    return BoolResult.Success;
                },
                extraEndMessage: r => $"DatabaseUpdates=[{databaseUpdates}] UpdatedDatabaseManifest=[{updatedDatabaseManifest}] DatabaseUpdateLeaseExpiry=[{_databaseUpdateLeaseExpiry}]");
        }

        /// <summary>
        /// Updates the manifest in blob storage to match the current records in the database
        /// </summary>
        internal Task<Result<PartitionCheckpointManifest>> UpdateManifestFromDatabaseRecordAsync(OperationContext context, bool force = false)
        {
            PartitionId lastPartition = default;
            return context.PerformOperationAsync(
                Tracer,
                () => Storage.ReadModifyWriteAsync<PartitionCheckpointManifest, List<PartitionId>>(
                    context,
                    Configuration.PartitionCheckpointManifestFileName,
                    manifest =>
                    {
                        var updatedPartitions = new List<PartitionId>();
                        manifest.SetPartitionCount(_partitionIds.Length);

                        if (ShouldUpdateDatabase || force)
                        {
                            foreach (var partitionId in _partitionIds)
                            {
                                lastPartition = partitionId;
                                var databaseRecord = TryGetDatabasePartitionRecord(partitionId)
                                    .ThrowIfFailure();

                                var manifestRecord = manifest[partitionId];
                                if (databaseRecord != manifestRecord)
                                {
                                    manifest[partitionId] = databaseRecord;
                                    updatedPartitions.Add(partitionId);
                                }
                            }
                        }

                        return (manifest, Result: updatedPartitions, Updated: updatedPartitions.Count != 0);
                    }),
                extraEndMessage: r => $" LastPartition={lastPartition} UpdatedPartitions=({r.GetValueOrDefault().Result?.Count ?? -1})[{string.Join(", ", r.GetValueOrDefault().Result ?? Enumerable.Empty<PartitionId>())}]")
                .AsAsync(r => r.NextState);
        }

        /// <summary>
        /// Update the partition in the database if it the update interval has elapsed by
        /// ingesting diff ssts. The flow is:
        /// -   Compute the chain of diffs needed to synchronize the database with the current state.
        ///     If chain cannot be found, a full sst with all information for partition is applied.
        /// -   Download the snapshot chain of sst files and delete stale snapshots
        /// -   Ingest the snapshot sst files
        /// </summary>
        private Task UpdateDatabasePartitionAsync(OperationContext context, PartitionId partitionId)
        {
            return Storage.UseBlockBlobAsync<Result<UpdatePartitionResult>>(context,
                GetPartitionOutputBlobName(partitionId),
                timeout: TimeSpan.FromSeconds(120),
                endMessageSuffix: r => $" Partition={partitionId}, UpdateResult={r.GetValueOrDefault()}",
                isCritical: true,
                useAsync: async (context, blobWrapper) =>
                {
                    var blob = new PartitionBlob(partitionId, blobWrapper);

                    // Check database which snapshot the database is currently at for the partition
                    var databaseRecordResult = TryGetDatabasePartitionRecord(partitionId);
                    if (!databaseRecordResult.TryGetValue(out var databaseRecord)
                        || databaseRecord == null)
                    {
                        // Error reading from database or no prior partition record found. Log warning and continue and use full sst.
                        Tracer.Warning(context, $"Partition {partitionId} record missing from database. RetrieveResult={databaseRecordResult}");
                    }

                    // Check whether its time for an update
                    var lastUpdateTime = databaseRecord?.CreationTime;
                    if (lastUpdateTime?.IsRecent(_clock.UtcNow, Configuration.PartitionsUpdateInterval) == true)
                    {
                        // Database was updated recently, skip updating partition.
                        return Result.Success(new UpdatePartitionResult(Updated: false, Record: databaseRecord, LastUpdateTime: lastUpdateTime))
                            .WithSuccessDiagnostics($"Database record from '{databaseRecord?.CreationTime}' is up to date. Next update will be >= {databaseRecord?.CreationTime + Configuration.PartitionsUpdateInterval}");
                    }

                    // Get the chain of snapshots to the database
                    var snapshotChain = await ComputeSnapshotChainLatestLastAsync(context, blob, databaseRecord, AsyncOut.Var<List<PartitionBlob>>(out var staleSnapshots));
                    if (snapshotChain.Count == 0)
                    {
                        return Result.Success(new UpdatePartitionResult(Updated: false, Record: databaseRecord, LastUpdateTime: lastUpdateTime))
                            .WithSuccessDiagnostics($"No new data is available for update.");
                    }

                    var directory = Database.CreateTempDirectory($"in_sst.{DateTime.Now.Ticks}");
                    long totalLength = 0;

                    // Download snapshot files
                    var downloadBlobsTask = _actionQueue.ForEachAsync(snapshotChain, (snapshotFile, index) =>
                    {
                        var snapshot = snapshotFile.Blob;
                        var messagePrefix = $"Partition={partitionId} SnapshotId={snapshot.SnapshotId} SnapshotTime={snapshot.Blob.SnapshotTime} BaseSnapshot={snapshot.BaseSnapshotId}";

                        return context.PerformOperationAsync(
                            Tracer,
                            async () =>
                            {
                                var length = await ReadContentPartitionAsync(
                                    context,
                                    snapshot,
                                    new FileReader(directory.Path / snapshotFile.FileName),
                                    snapshotFile.Kind,
                                    requiresMetadataFetch: false);

                                Interlocked.Add(ref totalLength, length);

                                return Result.Success(length);
                            },
                            caller: "DownloadPartitionSstFileAsync",
                            extraEndMessage: r => $"{messagePrefix} Path={snapshotFile.FileName} Length={r.GetValueOrDefault()} ChainIndex={index}")
                        .ThrowIfFailureAsync();
                    });

                    // Garbage collect stale snapshots
                    var garbageCollectSnapshotsTask = _actionQueue.ForEachAsync(staleSnapshots.Value, (snapshot, index) =>
                    {
                        var messagePrefix = $"Partition={partitionId} SnapshotId={snapshot.SnapshotId} SnapshotTime={snapshot.Blob.SnapshotTime} BaseSnapshot={snapshot.BaseSnapshotId}";

                        return context.PerformOperationWithTimeoutAsync<Result<bool>>(
                            Tracer,
                            timeout: Configuration.StorageInteractionTimeout,
                            operation: async context =>
                            {
                                bool deleted = await snapshot.DeleteIfExistsAsync(token: context.Token);
                                return Result.Success(deleted);
                            },
                            caller: "DeletePartitionSnapshotAsync",
                            extraEndMessage: r => $"{messagePrefix} Deleted={r.GetValueOrDefault()}")
                        .IgnoreFailure();
                    });

                    await Task.WhenAll(downloadBlobsTask, garbageCollectSnapshotsTask);

                    var currentSnapshot = snapshotChain.LastOrDefault()?.Blob;

                    // Ingest the snaphot files
                    context.PerformOperation(
                        Tracer,
                        () =>
                        {
                            // Snapshot chain is latest first. For RocksDb ingestion, later files
                            // take precedence so we should reverse the list
                            return Database.IngestMergeContentSstFiles(context, snapshotChain.Select(f => directory.Path / f.FileName));
                        },
                        caller: "IngestPartitionSstFiles",
                        messageFactory: r => $"Partition={partitionId} SnapshotId={snapshotChain.LastOrDefault()?.Blob.SnapshotId} Files={snapshotChain.Count} ByteLength={totalLength}")
                    .ThrowIfFailure();

                    // 
                    TryGetDatabasePartitionRecord(partitionId).TryGetValue(out databaseRecord);
                    Contract.Check(databaseRecord != null && databaseRecord?.SnapshotId == currentSnapshot.SnapshotId)
                        ?.Assert($"Expected database snapshot id '{currentSnapshot.SnapshotId}' but found '{databaseRecord?.SnapshotId}'");

                    return Result.Success(new UpdatePartitionResult(Updated: true, Record: databaseRecord, LastUpdateTime: lastUpdateTime));
                })
                .IgnoreFailure<Result<UpdatePartitionResult>>();
        }

        /// <summary>
        /// Computes the chain of snapshot sst files to apply to the base record in order
        /// to match the current content registration in blob storage
        /// </summary>
        private async Task<List<SnapshotSstFile>> ComputeSnapshotChainLatestLastAsync(
            OperationContext context,
            PartitionBlob blob,
            PartitionRecord? baseSnapshot,
            AsyncOut<List<PartitionBlob>> snapshotsToDelete)
        {
            var (snapshots, currentSnapshot) = await blob.ListSnapshotsAndCurrentAsync(context, baseSnapshot?.SnapshotId);
            var snapshotChain = new List<SnapshotSstFile>();
            if (currentSnapshot?.SnapshotId is not Guid snapshotId)
            {
                // No updates available for partition.
                return snapshotChain;
            }

            var isChainComplete = false;
            Dictionary<Guid, PartitionBlob> snapshotsById = snapshots
                .Where(s => s.Blob.IsSnapshot && s.SnapshotId != null)
                .ToDictionarySafe(s => s.SnapshotId.Value);

            // Trace back from current snapshot to the base snapshot
            while (snapshotsById.TryGetValue(snapshotId, out var snapshot))
            {
                isChainComplete = baseSnapshot != null && snapshot.BaseSnapshotId == baseSnapshot?.SnapshotId;
                snapshotChain.Add(new SnapshotSstFile(snapshot, IsFull: snapshot.BaseSnapshotId == null));
                if (isChainComplete
                    || snapshot.BaseSnapshotId == null
                    || baseSnapshot == null
                    || snapshotChain.Count > Configuration.MaxSnapshotChainLength)
                {
                    break;
                }

                snapshotId = snapshot.BaseSnapshotId.Value;
            }

            Contract.Assert(snapshotChain.Count > 0);

            if (!isChainComplete)
            {
                // Chain is incomplete (i.e. could not trace back to base snapshot). Just use latest snaphot
                // as a full snapshot.
                snapshotChain.Clear();
                snapshotChain.Add(new SnapshotSstFile(currentSnapshot, IsFull: true));

                Tracer.Warning(context, $"Partition {blob.PartitionId} could not complete snapshot chain. Using full snapshot={currentSnapshot.SnapshotId}.");
            }

            // Compute the timestamp of the earliest snapshot which should be retained
            var minRetainedTime = snapshots
                .Select(s => s.Blob.SnapshotTime)
                .Where(time => time != null)
                .OrderByDescending(s => s)
                .Take(Configuration.MaxRetainedSnapshots)
                .LastOrDefault();

            snapshotsToDelete.Value = snapshots
                // Don't delete blobs in the snapshot chain
                .Except(snapshotChain.Select(s => s.Blob))
                .Where(s => s.Blob.SnapshotTime != null)
                // Select the stale snapshots
                .Where(s => s.Blob.SnapshotTime < minRetainedTime)
                .ToList();

            // Snapshots are ordered latest to earliest at this point.
            // Reverse the snapshots so they are in the correct order for ingestion.
            // (i.e. snapshot with latest data should appear last).
            snapshotChain.Reverse();
            return snapshotChain;
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
            PartitionId partitionId,
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
                endMessageSuffix: _ => $" Partition={partitionId}, BlockId={LocalMachineRecord.MachineBlockId}, {partition}")
                .IgnoreFailure();
        }

        private record struct PartitionComputationResult(ContentListing Listing, int BlockCount, int RetainedBlockCount);

        /// <summary>
        /// Computes the sorted partition content from the submission blob
        /// </summary>
        public Task<ContentListing> ComputeSortedPartitionContentAsync(
            OperationContext context,
            PartitionId partitionId,
            bool takeLease = true)
        {
            return Storage.UseBlockBlobAsync<Result<PartitionComputationResult>>(context,
                GetPartitionSubmissionBlobName(partitionId),
                timeout: TimeSpan.FromSeconds(120),
                useAsync: async (context, b) =>
                {
                    var blob = new PartitionBlob(partitionId, b);
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

                    var listing = await ReadContentPartitionAsync(context, blob, ContentListingReader.Instance);

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
        /// Reads the content listing from the blob (with optional range)
        /// </summary>
        private Task<T> ReadContentPartitionAsync<T>(
            OperationContext context,
            PartitionBlob blob,
            IReader<T> reader,
            PartitionOutputKind? blockKind = null,
            AccessCondition accessCondition = null,
            bool requiresMetadataFetch = true)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    if (requiresMetadataFetch)
                    {
                        await blob.FetchAttributesAsync();
                    }

                    BlobRange? range = null;
                    if (blockKind != null)
                    {
                        range = blob[blockKind.Value];
                        if (range == null)
                        {
                            return new ErrorResult($"Block range '{blockKind}' not found in blob.");
                        }
                    }

                    var value = reader.Create(range?.Length ?? blob.Blob.Properties.Length);
                    bool disposeOnException()
                    {
                        (value as IDisposable)?.Dispose();
                        return false;
                    }
                    try
                    {
                        using var stream = reader.GetStream(value);
                        await blob.DownloadRangeToStreamAsync(
                            stream,
                            range?.Offset,
                            range?.Length,
                            accessCondition);
                        return Result.Success(value);
                    }
                    catch when (disposeOnException())
                    {
                        throw;
                    }
                },
                extraEndMessage: r => $"Partition={blob.PartitionId}, Blob={blob.Name}, Block={blockKind} Value={r.GetValueOrDefault()}")
                .ThrowIfFailureAsync(unwrap: true);
        }

        /// <summary>
        /// Processes the partition submission blob against the current base snapshot to create a new output blob.
        /// </summary>
        public Task ProcessPartitionAsync(
            OperationContext context,
            PartitionId partitionId)
        {
            string leaseId = null;
            return Storage.UseBlockBlobAsync<Result<UpdatePartitionResult>>(context,
                GetPartitionOutputBlobName(partitionId),
                timeout: TimeSpan.FromSeconds(120),
                useAsync: async (context, blobWrapper) =>
                {
                    var blob = new PartitionBlob(partitionId, blobWrapper);

                    if (!await blob.ExistsAsync())
                    {
                        await blob.UploadFromByteArrayAsync(
                            new ArraySegment<byte>(Array.Empty<byte>(), 0, 0),
                            AccessCondition.GenerateIfNotExistsCondition());
                    }

                    bool wasLeased = false;
                    var lastUpdateTime = blob.LastUpdateTime;
                    if (lastUpdateTime?.IsRecent(_clock.UtcNow, Configuration.PartitionsUpdateInterval) == true
                        || (wasLeased = blob.Inner.Blob.Properties.LeaseState == LeaseState.Leased))
                    {
                        // Do nothing if blob is up to date or current being updated (currently leased)
                        return Result.Success(new UpdatePartitionResult(Updated: false, Record: null, LastUpdateTime: lastUpdateTime, WasLeased: wasLeased));
                    }

                    // Acquire lease for maximum non-infinite duration (60s).
                    leaseId = await blob.AcquireLeaseAsync(TimeSpan.FromSeconds(60));
                    var leaseCondition = AccessCondition.GenerateLeaseCondition(leaseId);
                    blob = blob with { DefaultAccessCondition = leaseCondition };

                    var manifest = await Storage
                        .ReadAsync<PartitionCheckpointManifest>(context, Configuration.PartitionCheckpointManifestFileName)
                        .ThrowIfFailureAsync();

                    PartitionBlob baseSnapshot = null;
                    if (manifest[partitionId] is PartitionRecord baseRecord)
                    {
                        var snapshots = await blob.ListSnapshotsAsync(context);
                        baseSnapshot = snapshots.FirstOrDefault(b => b.SnapshotId == baseRecord.SnapshotId);
                    }

                    // Get base listing
                    using var baselineListing = baseSnapshot == null
                        ? ContentListing.CreateFromByteLength(0)
                        : await ReadContentPartitionAsync(
                            context,
                            baseSnapshot,
                            ContentListingReader.Instance,
                            PartitionOutputKind.FullListing,
                            AccessCondition.GenerateEmptyCondition());

                    using var currentListing = await ComputeSortedPartitionContentAsync(context, partitionId);

                    var blockList = new List<OutputBlock>();

                    var partitionRecord = new PartitionRecord(
                        PartitionId: partitionId,
                        CreationTime: _clock.UtcNow,
                        SnapshotId: Guid.NewGuid(),
                        BaseSnapshotCreationTime: baseSnapshot?.LastUpdateTime,
                        BaseSnapshotId: baseSnapshot?.SnapshotId,
                        new PartitionUpdateStatistics());

                    // Write the full and diff sst files
                    await CreateAndUploadSstFileBlocksAsync(
                        context,
                        blob,
                        blockList,
                        partitionRecord,
                        baseline: baselineListing,
                        current: currentListing).ThrowIfFailureAsync();

                    // Write the full listing blob
                    await PutPartitionOutputBlockAsync(
                        context,
                        blob,
                        PartitionOutputKind.FullListing,
                        blockList,
                        () => currentListing.AsStream());

                    // Finalize the blob
                    // Update metadata which will be associated with the block during PutBlockList
                    blob.SetInfo(partitionRecord);

                    await blob.PutBlockListAsync(
                        blockList.Select(b => b.BlockId));

                    await blob.ReleaseLeaseAsync(leaseCondition);

                    return Result.Success(new UpdatePartitionResult(Updated: true, partitionRecord, lastUpdateTime));
                },
                endMessageSuffix: r => $" Partition={partitionId}, Lease={leaseId}, UpdateResult={r.GetValueOrDefault()}")
                .IgnoreFailure<Result<UpdatePartitionResult>>();
        }

        /// <summary>
        /// Write out and upload the full and diff sst files given the baseline and current content listing.
        /// </summary>
        private Task<BoolResult> CreateAndUploadSstFileBlocksAsync(
            OperationContext context,
            PartitionBlob blob,
            List<OutputBlock> blockList,
            PartitionRecord record,
            ContentListing baseline,
            ContentListing current)
        {
            Contract.Assert(Database != null);

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    using var dir = Database.CreateTempDirectory("out_sst");

                    var fullSstPath = dir.Path / $"{blob.PartitionId}.full.sst";
                    var diffSstPath = dir.Path / $"{blob.PartitionId}.diff.sst";

                    context.PerformOperation(
                        Tracer,
                        () =>
                        {
                            using SstFileWriter fullSstWriter = Database.CreateContentSstWriter(context, fullSstPath).ThrowIfFailure();
                            using SstFileWriter diffSstWriter = Database.CreateContentSstWriter(context, diffSstPath).ThrowIfFailure();

                            var result = WriteSstFiles(
                                context,
                                blob.PartitionId,
                                baseline: baseline,
                                current: current,
                                fullSstWriter: fullSstWriter,
                                diffSstWriter: diffSstWriter,
                                record.Statistics);

                            // Write the database record as the last entry which contains the snapshot id and summary information
                            // about the content contained in the current listing and the difference with the baseline
                            WriteDatabasePartitionRecord(fullSstWriter, record);
                            WriteDatabasePartitionRecord(diffSstWriter, record);

                            fullSstWriter.Finish();
                            diffSstWriter.Finish();

                            return result;

                        }).ThrowIfFailure();

                    Task putSstFileBlockAsync(AbsolutePath path, PartitionOutputKind kind)
                    {
                        return PutPartitionOutputBlockAsync(context, blob, kind, blockList, () => _fileSystem.OpenReadOnly(path, FileShare.Read));
                    }

                    await Task.WhenAll(
                        putSstFileBlockAsync(fullSstPath, PartitionOutputKind.FullSst),
                        putSstFileBlockAsync(diffSstPath, PartitionOutputKind.DiffSst));

                    return BoolResult.Success;
                },
                extraEndMessage: r => $" Partition={blob.PartitionId}, Counters={record.Statistics}");
        }

        /// <summary>
        /// Puts a partition output block
        /// </summary>
        private Task PutPartitionOutputBlockAsync(
            OperationContext context,
            PartitionBlob blob,
            PartitionOutputKind kind,
            List<OutputBlock> blockList,
            Func<StreamWithLength> getStream)
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
                            stream);

                        lock (blockList)
                        {
                            var last = blockList.LastOrDefault().Range;
                            var range = new BlobRange(Offset: last.Offset + last.Length, Length: stream.Length);
                            blockList.Add(new(kind, range, blockId));
                            blob[kind] = range;
                        }

                        return BoolResult.Success;
                    }
                },
                caller: $"{nameof(PutPartitionOutputBlockAsync)}.{kind}",
                extraEndMessage: _ => $"Partition={blob.PartitionId} Range={blob[kind]}")
                .ThrowIfFailureAsync();
        }

        private static BlobName GetPartitionSubmissionBlobName(PartitionId partitionId)
        {
            return $"{partitionId.BlobPrefix}.bin";
        }

        private static BlobName GetPartitionOutputBlobName(PartitionId partitionId, DateTimeOffset? snapshotTime = null)
        {
            BlobName blobName = $"{partitionId.BlobPrefix}.out.bin";
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
        internal record PartitionCheckpointManifest
        {
            public PartitionRecord?[] Records { get; set; } = new PartitionRecord?[0];

            public void SetPartitionCount(int partitionCount)
            {
                if (Records.Length != partitionCount)
                {
                    Records = new PartitionRecord?[partitionCount];
                }
            }

            public PartitionRecord? this[PartitionId partitionId]
            {
                get
                {
                    if (Records.Length == partitionId.PartitionCount)
                    {
                        return Records[partitionId.Index];
                    }

                    return null;
                }
                set
                {
                    Contract.Check(partitionId.PartitionCount == Records.Length)?
                        .Assert($"{partitionId.PartitionCount} does not match {Records.Length}");

                    Records[partitionId.Index] = value;
                }
            }
        }

        /// <summary>
        /// Record of snapshot of a partition used for the checkpoint (blob storage uses this a the snapshot id)
        /// </summary>
        internal record struct PartitionRecord(
            PartitionId PartitionId,
            DateTime CreationTime,
            Guid SnapshotId,
            DateTime? BaseSnapshotCreationTime,
            Guid? BaseSnapshotId,
            PartitionUpdateStatistics Statistics);

        private record struct BlobRange(long Offset, long Length);

        private record struct UpdatePartitionResult(bool Updated, PartitionRecord? Record, DateTime? LastUpdateTime, bool? WasLeased = null);

        private record FileReader(AbsolutePath Path) : IReader<long>
        {
            public long Create(long byteLength)
            {
                return byteLength;
            }

            public Stream GetStream(long length)
            {
                return PassThroughFileSystem.Default.OpenForWrite(Path, length, FileMode.Create, FileShare.Delete);
            }
        }

        private class ContentListingReader : IReader<ContentListing>
        {
            public static readonly ContentListingReader Instance = new ContentListingReader();

            public ContentListing Create(long byteLength)
            {
                return ContentListing.CreateFromByteLength((int)byteLength);
            }

            public Stream GetStream(ContentListing value)
            {
                return value.AsStream();
            }
        }

        private interface IReader<T>
        {
            T Create(long byteLength);

            Stream GetStream(T value);
        }

        private record SnapshotSstFile(PartitionBlob Blob, bool IsFull) : IKeyedItem<Guid>
        {
            public string FileName
            {
                get
                {
                    var kind = IsFull ? "Full" : "Diff";
                    return $"{Blob.PartitionId}.{Blob.LastUpdateTime:o}.{kind}.sst".Replace(":", "");
                }
            }

            public PartitionOutputKind Kind => IsFull ? PartitionOutputKind.FullSst : PartitionOutputKind.DiffSst;

            public Guid GetKey() => Blob.SnapshotId.Value;
        }

        /// <summary>
        /// Wrapper for accessing typed values of blob metadata.
        /// 
        /// DO NOT RENAME properties as these are used as keys in metadata dictionary.
        /// </summary>
        private sealed record PartitionBlob(PartitionId PartitionId, BlobWrapper Inner) : BlobWrapper(Inner), IKeyedItem<Guid?>
        {
            /// <summary>
            /// The base snapshot id used when computing the difference blocks.
            /// </summary>
            public Guid? BaseSnapshotId
            {
                get => GetMetadataOrDefault<Guid?>(s => Guid.Parse(s));
                set => SetMetadata(value?.ToString());
            }

            /// <summary>
            /// Time of last update. Using a specific metadata value to allow for tests notion of time.
            /// </summary>
            public DateTime? LastUpdateTime
            {
                get => GetMetadataOrDefault(s => DateTime.Parse(s, null, DateTimeStyles.RoundtripKind));
                set => SetMetadata(value?.ToString("O"));
            }

            public BlobRange? this[PartitionOutputKind kind]
            {
                get => GetMetadataOrDefault<BlobRange?>(s => JsonUtilities.JsonDeserialize<BlobRange>(s), key: $"{kind}BlockRange");
                set => SetMetadata(value == null ? null : JsonUtilities.JsonSerialize(value.Value), key: $"{kind}BlockRange");
            }

            /// <summary>
            /// The unique identifier of the current snapshot
            /// </summary>
            public Guid? SnapshotId
            {
                get => GetMetadataOrDefault<Guid?>(s => Guid.Parse(s));
                set => SetMetadata(value?.ToString());
            }

            public void SetInfo(PartitionRecord record)
            {
                BaseSnapshotId = record.BaseSnapshotId;
                SnapshotId = record.SnapshotId;
                LastUpdateTime = record.CreationTime;
            }

            public async Task<(List<PartitionBlob> snapshots, PartitionBlob currentSnapshot)> ListSnapshotsAndCurrentAsync(OperationContext context, Guid? excludedSnapshotId)
            {
                if (!await ExistsAsync()
                    || SnapshotId is not Guid currentSnapshotId
                    || currentSnapshotId == excludedSnapshotId)
                {
                    // No updates are available.
                    return (new List<PartitionBlob>(), null);
                }

                var snapshots = await ListSnapshotsAsync(context);

                var currentSnapshot = snapshots.FirstOrDefault(s => s.SnapshotId == currentSnapshotId);
                if (currentSnapshot == null)
                {
                    (await TrySnapshotAsync(knownToExist: true)).TryGetValue(out currentSnapshot);
                    if (currentSnapshot != null)
                    {
                        snapshots.Add(currentSnapshot);
                    }
                }

                return (snapshots, currentSnapshot);
            }

            public Task<List<PartitionBlob>> ListSnapshotsAsync(OperationContext context)
            {
                return ListSnapshotsAsync(context, wrapper => new PartitionBlob(PartitionId, wrapper));
            }

            public PartitionBlob GetSnaphot(DateTimeOffset? snapshotTime)
            {
                return new PartitionBlob(PartitionId, Inner with
                {
                    Blob = new CloudBlockBlob(Blob.Uri, snapshotTime, Blob.ServiceClient),
                    Name = Inner.Name with { SnapshotTime = snapshotTime }
                });
            }

            public async Task<Optional<PartitionBlob>> TrySnapshotAsync(bool knownToExist = false)
            {
                if (!knownToExist && !await Blob.ExistsAsync())
                {
                    return default;
                }

                var result = new PartitionBlob(PartitionId, await SnapshotAsync());

                // Perform another existence query to fetch attributes
                if (!await result.ExistsAsync())
                {
                    return default;
                }

                return result;
            }

            public override int GetHashCode()
            {
                return RuntimeHelpers.GetHashCode(this);
            }

            public bool Equals(PartitionBlob? other)
            {
                return ReferenceEquals(this, other);
            }

            public Guid? GetKey()
            {
                return SnapshotId;
            }
        }

        private record struct OutputBlock(PartitionOutputKind Kind, BlobRange Range, string BlockId);

        private Result<PartitionRecord?> TryGetDatabasePartitionRecord(PartitionId partitionId)
        {
            return Database.TryDeserializeValue(
                partitionId.GetPartitionRecordKey(),
                RocksDbContentMetadataDatabase.Columns.SstMergeContent,
                static reader => JsonSerializer.Deserialize<PartitionRecord>(reader.Span, JsonUtilities.DefaultSerializationOptions))
                .Select(o => o.HasValue ? o.Value : default(PartitionRecord?), isNullAllowed: true);
        }

        private void WriteDatabasePartitionRecord(IRocksDbColumnWriter writer, PartitionRecord record)
        {
            var recordBytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonUtilities.DefaultSerializationOptions);
            writer.Put(record.PartitionId.GetPartitionRecordKey(), recordBytes);
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
            FullListing
        }

        /// <summary>
        /// Test hook.
        /// </summary>
        internal class TestObserver
        {
            internal virtual void OnPutBlock(PartitionId partitionId, ContentListing partition)
            {
            }
        }
    }
}
