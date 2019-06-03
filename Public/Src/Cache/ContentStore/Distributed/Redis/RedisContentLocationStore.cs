// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Timers;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Collections;
using StackExchange.Redis;
using static BuildXL.Cache.ContentStore.Distributed.Redis.RedisContentLocationStoreConstants;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// A content location based store that is backed by a Redis instance.
    /// </summary>
    internal class RedisContentLocationStore : RedisContentLocationStoreBase, IContentLocationStore
    {
        private readonly RedisDatabaseAdapter _contentRedisDatabaseAdapter;
        private readonly RedisDatabaseAdapter _machineLocationRedisDatabaseAdapter;
        private readonly IClock _clock;
        private readonly IContentHasher _contentHasher;
        private readonly RedisContentLocationStoreTracer _tracer;
        private readonly RedisBlobAdapter _blobAdapter;

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <summary>
        /// Bump time for expiry when content is touched.
        /// </summary>
        internal TimeSpan ContentHashBumpTime { get; set; }

        // In-memory caches
        private readonly ConcurrentDictionary<MachineId, MachineLocation> _locationsById = new ConcurrentDictionary<MachineId, MachineLocation>();
        private readonly ConcurrentDictionary<MachineLocation, MachineId> _idsByLocation = new ConcurrentDictionary<MachineLocation, MachineId>();
        private readonly ConcurrentDictionary<ContentHash, MachineId> _idsByHash = new ConcurrentDictionary<ContentHash, MachineId>();

        // Local Machine Information
        private readonly MachineLocation _localMachineLocation;
        private readonly string _localMachineLocationHash;
        private static readonly HashInfo _dataMigrationHashInfo = HashInfoLookup.Find(HashType.Vso0);

        // Test Flags
        internal bool DisableHeartbeat = false;
        internal bool DisableReplica = false;

        private IntervalTimer _identityTimer;

        /// <inheritdoc />
        public MachineReputationTracker MachineReputationTracker { get; private set; }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="RedisContentLocationStore"/> class.
        /// </summary>
        internal RedisContentLocationStore(
            RedisDatabaseAdapter contentRedisDatabaseAdapter,
            RedisDatabaseAdapter machineLocationRedisDatabaseAdapter,
            IClock clock,
            TimeSpan contentHashBumpTime,
            byte[] localMachineLocation,
            RedisContentLocationStoreConfiguration configuration)
        : base(clock, configuration)
        {
            Contract.Requires(contentRedisDatabaseAdapter != null);
            Contract.Requires(localMachineLocation != null);
            Contract.Requires(configuration != null);

            _contentRedisDatabaseAdapter = contentRedisDatabaseAdapter;
            _machineLocationRedisDatabaseAdapter = machineLocationRedisDatabaseAdapter;
            _clock = clock;
            ContentHashBumpTime = contentHashBumpTime;
            _contentHasher = HashInfoLookup.Find(HashType.SHA256).CreateContentHasher();
            _localMachineLocation = new MachineLocation(localMachineLocation);
            _localMachineLocationHash = _localMachineLocation.GetContentHashString(_contentHasher);

            _tracer = new RedisContentLocationStoreTracer(nameof(RedisContentLocationStore));

            _blobAdapter = new RedisBlobAdapter(_contentRedisDatabaseAdapter, TimeSpan.FromMinutes(Configuration.BlobExpiryTimeMinutes), Configuration.MaxBlobCapacity, _clock, Tracer);
        }

        public bool AreBlobsSupported => Configuration.BlobExpiryTimeMinutes > 0 && Configuration.MaxBlobCapacity > 0 && Configuration.MaxBlobSize > 0;

        public long MaxBlobSize => Configuration.MaxBlobSize;

        /// <summary>
        /// Gets or sets a value indicating whether to randomize replicas: test hook.
        /// </summary>
        internal bool RandomizeReplicas { get; set; } = true;

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            _tracer.Debug(context, $"{Tracer.Name}: RedisBatchSize=[{Configuration.RedisBatchPageSize}]");

            MachineReputationTracker = new MachineReputationTracker(context, _clock, Configuration.ReputationTrackerConfiguration, ResolveMachineLocation);

            var baseResult = await base.StartupCoreAsync(context);
            if (!baseResult)
            {
                return baseResult;
            }

            if (!DisableHeartbeat)
            {
                _identityTimer = new IntervalTimer(
                    () => UpdateIdentityAsync(context),
                    TimeSpan.FromMinutes(HeartbeatIntervalInMinutes),
                    message =>
                    {
                        _tracer.Debug(context, $"[{HeartbeatName}] {message}");
                    });
            }

            if (Configuration.GarbageCollectionEnabled)
            {
                Task.Run(() => GarbageCollectAsync(new OperationContext(context))).FireAndForget(context, "RedisGarbageCollection");
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var counterSet = GetCounters(context);

            _identityTimer?.Dispose();
            _contentHasher.Dispose();

            counterSet.LogOrderedNameValuePairs(s => _tracer.Debug(context, s));

            return base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> RegisterLocalLocationWithCentralStoreAsync(Context context, IReadOnlyList<ContentHashWithSize> contentHashes, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return UpdateBulkAsync(
                context,
                contentHashes.Select(c => new ContentHashWithSizeAndLocations(c.Hash, c.Size, new[] { _localMachineLocation })).ToList(),
                cts,
                urgencyHint,
                LocationStoreOption.UpdateExpiry);
        }

        /// <inheritdoc />
        public override CounterSet GetCounters(Context context)
        {
            var counterSet = new CounterSet();
            counterSet.Merge(_tracer.GetCounters(), $"{RedisContentLocationStoreTracer.Component}.");
            counterSet.Merge(_blobAdapter.GetCounters(), $"{RedisContentLocationStoreTracer.Component}.BlobAdapter.");
            counterSet.Merge(base.GetCounters(context), $"{RedisContentLocationStoreTracer.Component}.");
            counterSet.Merge(_contentRedisDatabaseAdapter.Counters.ToCounterSet(), $"{RedisContentLocationStoreTracer.Component}.Redis.");
            counterSet.Merge(_contentRedisDatabaseAdapter.GetRedisCounters(new OperationContext(context), Tracer, Counters[ContentLocationStoreCounters.InfoStats]), $"{RedisContentLocationStoreTracer.Component}.RedisInfo.");
            return counterSet;
        }

        /// <summary>
        /// Gets the status of a redis instance.
        /// </summary>
        public Task<Result<RedisInfoStats>> GetRedisInfoAsync(OperationContext context, string serverId = null, bool trace = true)
        {
            return _contentRedisDatabaseAdapter.GetRedisInfoAsync(context, Tracer, Counters[ContentLocationStoreCounters.InfoStats], serverId, trace);
        }

        /// <inheritdoc />
        public void ReportReputation(MachineLocation location, MachineReputation reputation) =>
            MachineReputationTracker.ReportReputation(location, reputation);

        /// <summary>
        /// Remove all redis records with empty machine id set.
        /// </summary>
        public async Task<BoolResult> GarbageCollectAsync(OperationContext context)
        {
            if (await TryAcquireGcRoleAsync(context))
            {
                var tasks = _contentRedisDatabaseAdapter.GetServerKeys()
                    .Select(tpl => Task.Run(() => GarbageCollectShardAsync(context.CreateNested(), tpl.serverId, tpl.serverSampleKey))).ToArray();

                var results = await Task.WhenAll(tasks);
                return results.FirstOrDefault(r => !r) ?? BoolResult.Success;
            }

            return BoolResult.Success;
        }

        private async Task<bool> TryAcquireGcRoleAsync(OperationContext context)
        {
            var localMachineName = _localMachineLocation.ToString();
            var gcRoleAcquisitionResult = await _contentRedisDatabaseAdapter.ExecuteBatchAsync(context, batch => batch.AcquireMasterRoleAsync(
                    masterRoleRegistryKey: Configuration.GarbageCollectionConfiguration.GarbageCollectionLeaseKey,
                    machineName: localMachineName,
                    currentTime: _clock.UtcNow,
                    leaseExpiryTime: Configuration.GarbageCollectionConfiguration.GarbageCollectionInterval,
                    // Only 1 lease is allowed.
                    slotCount: 1,

                    // NOTE: We never explicitly release to prevent machines from initiating GC before the GC interval has expired
                    release: false
                ), RedisOperation.UpdateRole);

            if (gcRoleAcquisitionResult != null)
            {
                var priorMachineName = gcRoleAcquisitionResult.Value.PriorMasterMachineName;
                Tracer.Debug(context, $"'{localMachineName}' acquired GC role from '{priorMachineName}', PriorStatus: '{gcRoleAcquisitionResult?.PriorMachineStatus}', LastHeartbeat: '{gcRoleAcquisitionResult?.PriorMasterLastHeartbeat}'");
                return true;
            }

            return false;
        }

        private struct GarbageCollectionResult
        {
            public long NextCursor;
            public int CleanedKeys;
            public int CleanableKeys;
            public int EntriesCount;
            public int MalformedKeys;
            public int IterationsCount;
            public List<(string okey, ContentHash? key, ContentLocationEntry value)> DeserializedEntries;

            /// <nodoc />
            public static GarbageCollectionResult operator +(GarbageCollectionResult left, GarbageCollectionResult right)
            {
                return new GarbageCollectionResult()
                {
                    CleanedKeys = left.CleanedKeys + right.CleanedKeys,
                    CleanableKeys = left.CleanableKeys = right.CleanableKeys,
                    EntriesCount = left.EntriesCount + right.EntriesCount,
                    MalformedKeys = left.MalformedKeys + right.MalformedKeys,
                };
            }
        }

        private Task<BoolResult> GarbageCollectShardAsync(OperationContext context, string serverId, string sampleKey)
        {
            // We're using the following trick here:
            // In order to GC a specific shard we use a sample key that will allow us to 'forward' the request to appropriate shard.
            // The sample key is used only for this purposes and never appear in the lua-scripts or any other logic.            
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    context.TraceDebug($"Garbage collecting serverId: {serverId} sample key: {sampleKey}");

                    var config = Configuration.GarbageCollectionConfiguration;
                    int iterationsInBatch = config.IterationsInBatch;

                    // 0 means start from the beginning.
                    long cursor = 0;
                    int iteration = 0;
                    var shardStats = new GarbageCollectionResult();
                    while (!context.Token.IsCancellationRequested)
                    {
                        iteration++;

                        var possibleBatchResult = await context.PerformOperationAsync(
                            Tracer,
                            () => GarbageCollectBatchAsync(context, sampleKey, cursor),
                            Counters[ContentLocationStoreCounters.RedisGc_GarbageCollectBatch],
                            traceErrorsOnly: true);

                        if (!possibleBatchResult)
                        {
                            return new BoolResult(possibleBatchResult);
                        }

                        GarbageCollectionResult batchResult = possibleBatchResult.Value;

                        int currentCount = (iteration - 1) * iterationsInBatch + batchResult.IterationsCount;

                        Counters[ContentLocationStoreCounters.RedisGc_EntriesCount].Add(batchResult.EntriesCount);
                        Counters[ContentLocationStoreCounters.RedisGc_CleanedKeysCount].Add(batchResult.CleanedKeys);
                        Counters[ContentLocationStoreCounters.RedisGc_CleanableKeysCount].Add(batchResult.CleanedKeys);
                        Counters[ContentLocationStoreCounters.RedisGc_MalformedKeysCount].Add(batchResult.MalformedKeys);

                        shardStats += batchResult;

                        var shardInfo = await GetRedisInfoAsync(context, serverId, trace: false);

                        Tracer.Info(
                            context,
                            $"Garbage collection iteration {currentCount}: " +
                            string.Join(", ",
                                $"Entries: {batchResult.EntriesCount} of {shardStats.EntriesCount}",
                                $"Cleaned Keys: {batchResult.CleanedKeys} of {shardStats.CleanedKeys}",
                                $"Cleanable Keys: {batchResult.CleanableKeys} of {shardStats.CleanableKeys}",
                                shardInfo.Succeeded ? shardInfo.Value.ToDisplayString() : "<Shard info is not available>"));

                        if (batchResult.MalformedKeys != 0)
                        {
                            Tracer.Debug(context, $"Malformed Keys: {batchResult.MalformedKeys} of {shardStats.MalformedKeys}");
                        }

                        cursor = batchResult.NextCursor;

                        if (cursor == 0)
                        {
                            // The garbage collection is done.
                            break;
                        }

                        if (config.DelayBetweenBatches != TimeSpan.Zero)
                        {
                            Tracer.Debug(context, $"Waiting between GC iterations for {config.DelayBetweenBatches.TotalMilliseconds}ms.");
                            await Task.Delay(config.DelayBetweenBatches);
                        }
                    }

                    return BoolResult.Success;
                },
                Counters[ContentLocationStoreCounters.RedisGc_GarbageCollectShard],
                traceOperationStarted: false);
        }

        private async Task<Result<GarbageCollectionResult>> GarbageCollectBatchAsync(OperationContext context, string sampleKey, long cursor)
        {
            int iteration = 0;
            var config = Configuration.GarbageCollectionConfiguration;
            int interationsInBatch = config.IterationsInBatch;
            var currentTime = _clock.UtcNow;

            var stats = new GarbageCollectionResult();
            while (!context.Token.IsCancellationRequested && iteration < interationsInBatch)
            {
                iteration++;

                var scanResult = await _contentRedisDatabaseAdapter.ExecuteBatchAsync(
                    context,
                    batch => batch.ScanAsync(sampleKey, cursor, config.ScanBatchSize),
                    RedisOperation.ScanEntriesWithLastAccessTime);

                var entries = await _contentRedisDatabaseAdapter.ExecuteBatchAsync(
                    context,
                    batch => batch.GetOrCleanAsync(sampleKey, scanResult.Keys, config.MaximumEntryLastAccessTime, whatIf: false),
                    RedisOperation.ScanEntriesWithLastAccessTime);

                stats.CleanedKeys += entries.ActualDeletedKeysCount;
                stats.CleanableKeys += entries.DeletedKeys.Length;
                stats.EntriesCount += entries.Entries.Length;

                var deserializedEntries =
                    entries.Entries
                    .Select(tpl => (okey: tpl.key, key: FromContentHashString(tpl.key), value: ContentLocationEntry.TryCreateFromRedisValue(tpl.value, currentTime.ToUnixTime())))
                    .ToList();

                // Printing malformed keys for debugging purposes.
                foreach (var malformed in deserializedEntries.Where(tpl => tpl.key == null).Take(1))
                {
                    context.TraceDebug($"MalformedKey: '{malformed.okey}'");
                }

                stats.MalformedKeys += deserializedEntries.Count(tpl => tpl.key == null || tpl.value == null);
                stats.DeserializedEntries = deserializedEntries;

                cursor = scanResult.Cursor;
                if (cursor == 0)
                {
                    break;
                }
            }

            stats.NextCursor = cursor;
            stats.IterationsCount = iteration;

            return stats;
        }

        /// <inheritdoc />
        public Task<BoolResult> UpdateBulkAsync(
            Context context,
            IReadOnlyList<ContentHashWithSizeAndLocations> contentHashesWithSizeAndLocations,
            CancellationToken cts,
            UrgencyHint urgencyHint,
            LocationStoreOption locationStoreOption)
        {
            if (contentHashesWithSizeAndLocations.Count == 0)
            {
                return BoolResult.SuccessTask;
            }

            return UpdateBulkCall.RunAsync(
                _tracer,
                new OperationContext(context),
                contentHashesWithSizeAndLocations,
                LocalMachineId,
                async () =>
                {
                    await UpdateBulkInternalAsync(context, contentHashesWithSizeAndLocations, cts, locationStoreOption);
                    return BoolResult.Success;
                });
        }

        /// <inheritdoc />
        public Task<GetBulkLocationsResult> GetBulkAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint,
            GetBulkOrigin origin)
        {
            if (origin == GetBulkOrigin.Local)
            {
                return GetBulkFromLocalAsync(context, contentHashes, cts, urgencyHint);
            }

            return GetBulkCall.RunAsync(
                _tracer,
                new OperationContext(context), // TODO: should this type implement StartupShutdownBase?
                contentHashes,
                () => GetBulkInternalAsync(context, contentHashes, cts, urgencyHint));
        }

        private Task<GetBulkLocationsResult> GetBulkFromLocalAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return Task.FromResult(new GetBulkLocationsResult(contentHashes.Select(h => new ContentHashWithSizeAndLocations(h)).ToList()));
        }

        private async Task ResolveLocationsAsync(OperationContext context, IReadOnlyList<ContentHash> hashes, List<ContentLocationEntry> entries, List<ContentHashWithSizeAndLocations> results)
        {
            Contract.Requires(hashes.Count == entries.Count);
            using (Counters[ContentLocationStoreCounters.ResolveLocations].Start())
            {
                var unresolvedMachineIds = new HashSet<MachineId>();
                foreach (var entry in entries)
                {
                    if (!entry.IsMissing)
                    {
                        foreach (var machineId in entry.Locations.EnumerateMachineIds())
                        {
                            if (!_locationsById.ContainsKey(machineId))
                            {
                                unresolvedMachineIds.Add(machineId);
                            }
                        }
                    }
                }

                if (unresolvedMachineIds.Count != 0)
                {
                    using (Counters[ContentLocationStoreCounters.ResolveLocations_GetLocationMapping].Start())
                    {
                        foreach (var unresolvedMachineId in unresolvedMachineIds.ToList())
                        {
                            var machineLocation = await GetLocationMappingAsync(context, unresolvedMachineId, context.Token);
                            if (machineLocation != null)
                            {
                                unresolvedMachineIds.Remove(unresolvedMachineId);
                            }
                        }
                    }
                }

                using (Counters[ContentLocationStoreCounters.ResolveLocations_CreateContentHashWithSizeAndLocations].Start())
                {
                    for (int i = 0; i < hashes.Count; i++)
                    {
                        // Remove any machine ids which could not be resolved
                        var resolvedMachineIds = entries[i].Locations.SetExistence(unresolvedMachineIds.AsReadOnlyCollection(), exists: false);
                        results.Add(
                            CreateContentHashWithSizeAndLocations(
                                hashes[i],
                                entries[i].ContentSize,
                                resolvedMachineIds));
                    }
                }
            }
        }

        private ContentHashWithSizeAndLocations CreateContentHashWithSizeAndLocations(ContentHash contentHash, long size, MachineIdSet machineSet)
        {
            IReadOnlyList<MachineLocation> locations = null;
            if (machineSet != null && !machineSet.IsEmpty)
            {
                locations = new MachineList(machineSet, machineId => ResolveMachineLocation(machineId), MachineReputationTracker, RandomizeReplicas);
            }

            return new ContentHashWithSizeAndLocations(contentHash, size, locations);
        }

        /// <inheritdoc />
        public Task<BoolResult> TrimBulkAsync(
            Context context,
            IReadOnlyList<ContentHashAndLocations> contentHashesAndLocations,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return TrimBulkRemoteCall.RunAsync(
                _tracer,
                new OperationContext(context),
                contentHashesAndLocations,
                () => TrimBulkRemoteInternalAsync(context, contentHashesAndLocations, cts));
        }

        /// <inheritdoc />
        public Task<BoolResult> TrimBulkAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return TrimBulkLocalCall.RunAsync(
                _tracer,
                new OperationContext(context),
                contentHashes,
                LocalMachineId,
                async () => await TrimBulkLocalInternalAsync(context, contentHashes, cts));
        }

        /// <inheritdoc />
        public Task<ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>>> TrimOrGetLastAccessTimeAsync(
            Context context,
            IList<Tuple<ContentHashWithLastAccessTimeAndReplicaCount, bool>> contentHashesWithInfo,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return TrimOrGetLastAccessTimeCall.RunAsync(
                _tracer,
                new OperationContext(context),
                contentHashesWithInfo,
                LocalMachineId,
                () => TrimOrGetLastAccessTimeInternalAsync(context, contentHashesWithInfo, cts));
        }

        /// <inheritdoc />
        public Task<BoolResult> TouchBulkAsync(
            Context context,
            IReadOnlyList<ContentHashWithSize> contentHashesWithSize,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return TouchBulkCall.RunAsync(
                _tracer,
                new OperationContext(context),
                contentHashesWithSize,
                LocalMachineId,
                async () =>
                {
                    await TouchBulkInternalAsync(context, contentHashesWithSize, notifyEventStore: true, cts: cts);
                    return BoolResult.Success;
                });
        }

        protected override async Task TouchBulkInternalAsync(
            Context context,
            IReadOnlyList<ContentHashWithSize> contentHashesWithSize,
            bool notifyEventStore,
            CancellationToken cts)
        {
            DateTime newExpiryTime = _clock.UtcNow.Add(ContentHashBumpTime);
            var localMachineId = await GetLocalLocationIdAsync(context, cts);

            foreach (var hashesWithSize in contentHashesWithSize.GetPages(Configuration.RedisBatchPageSize))
            {
                IRedisBatch batch = _contentRedisDatabaseAdapter.CreateBatchOperation(RedisOperation.TouchBulk);

                var touchTime = _clock.UtcNow;
                foreach (var hashWithSize in hashesWithSize)
                {
                    string contentHashString = GetContentHashString(hashWithSize.Hash);
                    byte[] sizeInBytes = ConvertLongToBytes(hashWithSize.Size);

                    batch.TouchOrSetLocationRecordAsync(contentHashString, sizeInBytes, localMachineId.GetContentLocationEntryBitOffset(), newExpiryTime, touchTime).FireAndForget(context);
                }

                await _contentRedisDatabaseAdapter.ExecuteBatchOperationAsync(context, batch, cts).ThrowIfFailure();
            }
        }

        private async Task<BoolResult> TrimBulkLocalInternalAsync(
            Context context,
            IEnumerable<ContentHash> contentHashes,
            CancellationToken cts)
        {
            var localMachineId = await GetLocalLocationIdAsync(context, cts);
            var hashesUpdated = 0;

            foreach (var hashes in contentHashes.GetPages(Configuration.RedisBatchPageSize))
            {
                IRedisBatch batch = _contentRedisDatabaseAdapter.CreateBatchOperation(RedisOperation.TrimBulk);
                foreach (var hash in hashes)
                {
                    string contentHashString = GetContentHashString(hash);
                    batch.SetBitIfExistAndRemoveIfEmptyBitMaskAsync(contentHashString, localMachineId.GetContentLocationEntryBitOffset(), false).FireAndForget(context);
                }

                await _contentRedisDatabaseAdapter.ExecuteBatchOperationAsync(context, batch, cts).ThrowIfFailure();
                hashesUpdated += hashes.Count;
            }

            _tracer.Debug(context, $"Hashes updated in TrimBulkLocalInternalAsync: {hashesUpdated}");
            return BoolResult.Success;
        }

        private async Task<ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>>> TrimOrGetLastAccessTimeInternalAsync(
            Context context,
            IList<Tuple<ContentHashWithLastAccessTimeAndReplicaCount, bool>> contentHashesWithInfo,
            CancellationToken cts)
        {
            IRedisBatch batch = _contentRedisDatabaseAdapter.CreateBatchOperation(RedisOperation.TrimOrGetLastAccessTime);
            IList<Tuple<ContentHash, Task<RedisLastAccessTimeResult>>> hashToUnixRedisLastAccessTime = new List<Tuple<ContentHash, Task<RedisLastAccessTimeResult>>>();

            var localMachineId = await GetLocalLocationIdAsync(context, cts);

            var currentTime = _clock.UtcNow;
            var operationContext = new OperationContext(context, cts);
            foreach (var contentHashWithLastAccessTimeAndCheck in contentHashesWithInfo)
            {
                var contentHashWithLastAccessTime = contentHashWithLastAccessTimeAndCheck.Item1;
                var checkReplicas = contentHashWithLastAccessTimeAndCheck.Item2;

                string contentHashString = GetContentHashString(contentHashWithLastAccessTime.ContentHash);
                var accessTimeTask = batch.TryTrimWithLastAccessTimeCheckAsync(
                    contentHashString,
                    currentTime,
                    contentHashWithLastAccessTime.LastAccessTime,
                    ContentHashBumpTime,
                    TargetRange,
                    localMachineId.GetContentLocationEntryBitOffset(),
                    checkReplicas ? Configuration.MinReplicaCountToSafeEvict : 0,
                    Configuration.MinReplicaCountToImmediateEvict).FireAndForgetAndReturnTask(context);

                hashToUnixRedisLastAccessTime.Add(Tuple.Create(contentHashWithLastAccessTime.ContentHash, accessTimeTask));
            }

            await _contentRedisDatabaseAdapter.ExecuteBatchOperationAsync(context, batch, cts).ThrowIfFailure();

            var hashToLastUseTimeMap = new List<ContentHashWithLastAccessTimeAndReplicaCount>(hashToUnixRedisLastAccessTime.Count);

            foreach (var hashWithTask in hashToUnixRedisLastAccessTime)
            {
                var lastAccessTimeResult = await hashWithTask.Item2;
                var lastAccessTime = lastAccessTimeResult.LastAccessTime;
                bool safeToEvict = lastAccessTimeResult.SafeToEvict;
                var locationCount = lastAccessTimeResult.LocationCount;
                
                var contentHashInfo = new ContentHashWithLastAccessTimeAndReplicaCount(
                    hashWithTask.Item1,
                    lastAccessTime,
                    locationCount,
                    safeToEvict);

                hashToLastUseTimeMap.Add(contentHashInfo);
            }

            return new ObjectResult<IList<ContentHashWithLastAccessTimeAndReplicaCount>>(hashToLastUseTimeMap);
        }

        private async Task<GetBulkLocationsResult> GetBulkInternalAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            var finalResults = new List<ContentHashWithSizeAndLocations>();
            var hashToContentLocationMap = new Dictionary<ContentHash, Task<RedisResult>>();

            DateTime newExpiry = _clock.UtcNow + ContentHashBumpTime;
            foreach (IList<ContentHash> contentHashBatch in contentHashes.GetPages(Configuration.RedisBatchPageSize))
            {
                var hashToContentLocationMapWithRedisValue = await GetContentLocationMapAsync(
                    context,
                    contentHashBatch,
                    hashToContentLocationMap,
                    newExpiry,
                    cts);

                IReadOnlyList<ContentHashWithSizeAndLocations> locations = await CompileLocationsAsync(
                    context,
                    contentHashBatch,
                    hashToContentLocationMapWithRedisValue,
                    cts,
                    urgencyHint);

                finalResults.AddRange(locations);
            }

            return new GetBulkLocationsResult(finalResults);
        }

        private async Task<Dictionary<ContentHash, Task<RedisValue>>> GetContentLocationMapAsync(
            Context context,
            IList<ContentHash> contentHashBatch,
            Dictionary<ContentHash, Task<RedisResult>> hashToContentLocationMap,
            DateTime newExpiry,
            CancellationToken cts)
        {
            IRedisBatch batch = _contentRedisDatabaseAdapter.CreateBatchOperation(RedisOperation.GetContentLocationMap); 
            foreach (ContentHash hash in contentHashBatch)
            {
                string contentHashString = GetContentHashString(hash);
                if (!hashToContentLocationMap.ContainsKey(hash))
                {
                    hashToContentLocationMap[hash] =
                        batch.StringGetAndUpdateExpiryAsync(contentHashString, newExpiry, _clock.UtcNow)
                            .FireAndForgetAndReturnTask(context);
                }
            }

            await _contentRedisDatabaseAdapter.ExecuteBatchOperationAsync(context, batch, cts).ThrowIfFailure();

            // Process replicas
            var hashToContentLocationMapWithRedisValue = new Dictionary<ContentHash, Task<RedisValue>>();

            foreach (var hash in contentHashBatch)
            {
                RedisResult redisResult = await hashToContentLocationMap[hash];
                hashToContentLocationMapWithRedisValue[hash] = Task.FromResult((RedisValue)redisResult);
            }

            return hashToContentLocationMapWithRedisValue;
        }

        /// <summary>
        /// Helper method for extracting locations from Redis.
        /// </summary>
        private async Task<IReadOnlyList<ContentHashWithSizeAndLocations>> CompileLocationsAsync(
            Context context,
            IList<ContentHash> contentHashes,
            Dictionary<ContentHash, Task<RedisValue>> hashToContentLocationMap,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            using (Counters[ContentLocationStoreCounters.CompileLocations].Start())
            {
                var finalResults = new List<ContentHashWithSizeAndLocations>();
                await ProcessLocationMappingsAsync(context, contentHashes, hashToContentLocationMap, cts, finalResults);

                // If getting locations during replication, check master for most updated info when no locations found at replica.
                // UrgencyHint.Low is used in testing to skip checking master despite being below UrgencyHint.Nominal
                if (urgencyHint == UrgencyHint.Minimum)
                {
                    finalResults = await CheckMasterForLocationsAsync(context, finalResults, cts);
                }

                return finalResults;
            }
        }

        private async Task<IList<ContentHashWithExistence>> CompileExistenceAsync(
            Context context,
            IList<ContentHash> contentHashes,
            Dictionary<ContentHash, Task<RedisValue>> hashToContentLocationMap,
            CancellationToken cts)
        {
            var finalResults = new List<ContentHashWithExistence>();

            foreach (ContentHash contentHash in contentHashes)
            {
                bool exists = false;

                RedisValue sizeAndContentLocations = await hashToContentLocationMap[contentHash];
                if (sizeAndContentLocations != RedisValue.Null)
                {
                    exists = await FindExistenceOfLocationMappingAsync(sizeAndContentLocations, cts, context);
                }

                finalResults.Add(new ContentHashWithExistence(contentHash, exists));
            }

            return finalResults;
        }

        private async Task<List<ContentHashWithSizeAndLocations>> CheckMasterForLocationsAsync(
            Context context,
            IList<ContentHashWithSizeAndLocations> replicaResults,
            CancellationToken cts)
        {
            const UrgencyHint masterHint = UrgencyHint.Nominal;
            IRedisBatch batch = _contentRedisDatabaseAdapter.CreateBatchOperation(RedisOperation.CheckMasterForLocations);
            var hashToContentLocationMap = new Dictionary<ContentHash, Task<RedisValue>>();
            var finalResults = new List<ContentHashWithSizeAndLocations>();

            foreach (var result in replicaResults)
            {
                if (result.Locations.NullOrEmpty())
                {
                    string contentHashString = GetContentHashString(result.ContentHash);
                    hashToContentLocationMap[result.ContentHash] =
                        batch.StringGetAsync(contentHashString, GetCommandFlagsForUrgencyHint(masterHint));
                }
                else
                {
                    finalResults.Add(result);
                }
            }

            await _contentRedisDatabaseAdapter.ExecuteBatchOperationAsync(context, batch, cts).ThrowIfFailure();

            await ProcessLocationMappingsAsync(context, hashToContentLocationMap.Keys, hashToContentLocationMap, cts, finalResults);

            return finalResults;
        }

        private async Task ProcessLocationMappingsAsync(
            Context context,
            IEnumerable<ContentHash> contentHashes,
            Dictionary<ContentHash, Task<RedisValue>> hashToContentLocationMap,
            CancellationToken cts,
            List<ContentHashWithSizeAndLocations> results)
        {
            using (Counters[ContentLocationStoreCounters.ProcessLocationMappings].Start())
            {
                var operationContext = new OperationContext(context, cts);
                var hashes = new List<ContentHash>(contentHashes);
                var entries = new List<ContentLocationEntry>(hashes.Count);

                foreach (ContentHash contentHash in hashes)
                {
                    RedisValue sizeAndContentLocations = await hashToContentLocationMap[contentHash];
                    ContentLocationEntry entry = ContentLocationEntry.Missing;
                    if (sizeAndContentLocations != RedisValue.Null)
                    {
                        entry = ToContentLocationEntry(sizeAndContentLocations);
                    }

                    entries.Add(entry);
                }

                await ResolveLocationsAsync(operationContext, hashes, entries, results);
            }
        }

        /// <summary>
        /// Helper method to add replicas for given content hashes
        /// </summary>
        /// <remarks>
        /// Expiry times for content is bumped when new replicas are added
        /// </remarks>
        private async Task UpdateBulkInternalAsync(
            Context context,
            IReadOnlyList<ContentHashWithSizeAndLocations> contentHashesWithSizeAndLocations,
            CancellationToken cts,
            LocationStoreOption locationStoreOption)
        {
            DateTime newExpiryTime = _clock.UtcNow.Add(ContentHashBumpTime);

            foreach (var contentHashBatch in contentHashesWithSizeAndLocations.GetPages(Configuration.RedisBatchPageSize))
            {
                IRedisBatch batch = _contentRedisDatabaseAdapter.CreateBatchOperation(RedisOperation.UpdateBulk);

                foreach (var hashInfo in contentHashBatch)
                {
                    string contentHashString = GetContentHashString(hashInfo.ContentHash);

                    // Add or update size and expiry when not during replication.
                    // Replication should not re-register a hash in redis and shouldn't extend lifetime.
                    if (locationStoreOption != LocationStoreOption.None)
                    {
                        if (hashInfo.Size >= 0)
                        {
                            byte[] sizeInBinary = ConvertLongToBytes(hashInfo.Size);
                            batch.StringSetRangeAndBumpExpiryAsync(contentHashString, 0, sizeInBinary, newExpiryTime, _clock.UtcNow)
                                .FireAndForget(context);
                        }
                        else
                        {
                            batch.SetExpiryAsync(contentHashString, newExpiryTime, _clock.UtcNow).FireAndForget(context);
                        }
                    }

                    // TODO: This ends up being in memory but this code doesn’t know that so this await is not async. (bug 1365340)
                    await UpdateContentLocationsInRedisAsync(
                        context,
                        batch,
                        contentHashString,
                        hashInfo.Locations,
                        cts,
                        locationStoreOption);
                }

                await _contentRedisDatabaseAdapter.ExecuteBatchOperationAsync(context, batch, cts).ThrowIfFailure();
            }
        }

        /// <summary>
        /// Helper method to remove replicas for given content hashes
        /// </summary>
        private async Task<BoolResult> TrimBulkRemoteInternalAsync(
            Context context,
            IReadOnlyList<ContentHashAndLocations> contentHashesAndLocations,
            CancellationToken cts)
        {
            IRedisBatch trimBatch = _contentRedisDatabaseAdapter.CreateBatchOperation(RedisOperation.TrimBulkRemote);
            foreach (ContentHashAndLocations hashInfo in contentHashesAndLocations)
            {
                string contentHashString = GetContentHashString(hashInfo.ContentHash);
                var locationIds = await GetLocationIdsAsync(context, hashInfo.Locations, cts);

                foreach (var locationId in locationIds)
                {
                    // The tasks are part of a batch operation and are awaited when ExecuteBatchOperationAsync is called
                    trimBatch.SetBitIfExistAndRemoveIfEmptyBitMaskAsync(contentHashString, locationId.GetContentLocationEntryBitOffset(), false).FireAndForget(context);
                }
            }

            return await _contentRedisDatabaseAdapter.ExecuteBatchOperationAsync(context, trimBatch, cts);
        }

        private async Task<bool> FindExistenceOfLocationMappingAsync(
            byte[] contentLocationsByteArray,
            CancellationToken cts,
            Context context)
        {
            for (int i = BytesInFileSize; i < contentLocationsByteArray.Length; i++)
            {
                byte redisChar = contentLocationsByteArray[i];

                int position = 0;
                while (redisChar != 0)
                {
                    if ((redisChar & MaxCharBitMask) != 0)
                    {
                        var found = await GetLocationMappingAsync(context, position, i, cts);
                        if (found != null)
                        {
                            return true;
                        }
                    }

                    redisChar <<= 1;
                    position++;
                }
            }

            return false;
        }

        private Task<MachineLocation?> GetLocationMappingAsync(Context context, int position, int index, CancellationToken cts, int offset = BytesInFileSize)
        {
            // Subtract 8 to account for file size stored in Redis
            var contentLocationId = new MachineId(((index - offset) * 8) + position);

            return GetLocationMappingAsync(context, contentLocationId, cts);
        }

        private async Task<MachineLocation?> GetLocationMappingAsync(Context context, MachineId contentLocationId, CancellationToken cts)
        {
            if (!_locationsById.TryGetValue(contentLocationId, out var machineLocation))
            {
                var machineLocationData =
                    await _machineLocationRedisDatabaseAdapter.StringGetAsync(
                        context,
                        $"{ContentLocationIdPrefix}{contentLocationId}",
                        cts);
                if (machineLocationData == RedisValue.Null)
                {
                    _tracer.Error(context, $"Unexpected empty content location data at content location id {contentLocationId}");
                    return null;
                }

                machineLocation = new MachineLocation((byte[])machineLocationData);
                _tracer.Debug(context, $"Retrieved location mapping: {contentLocationId}={machineLocation}");

                _locationsById.TryAdd(contentLocationId, machineLocation);
                _idsByLocation.TryAdd(machineLocation, contentLocationId);
            }

            return machineLocation;
        }

        private async Task<IReadOnlyList<MachineId>> GetLocationIdsAsync(Context context, IReadOnlyList<MachineLocation> locations, CancellationToken cts)
        {
            IReadOnlyList<MachineId> locationIds;
            if (locations == null)
            {
                locationIds = new[] { await GetLocalLocationIdAsync(context, cts) };
            }
            else
            {
                locationIds = await GetLocationIdsForRemoteLocationsAsync(context, locations, cts);
            }

            return locationIds;
        }

        protected override async Task<MachineId> GetLocalLocationIdAsync(Context context, CancellationToken cts)
        {
            if (!LocalMachineId.HasValue)
            {
                await UpdateIdentityAsync(context);
                Contract.Assert(LocalMachineId.HasValue);
            }

            return LocalMachineId.Value;
        }

        private async Task UpdateContentLocationsInRedisAsync(
            Context context,
            IRedisBatch batch,
            string hash,
            IReadOnlyList<MachineLocation> locations,
            CancellationToken cts,
            LocationStoreOption locationStoreOption)
        {
            var locationIds = await GetLocationIdsAsync(context, locations, cts);

            foreach (var locationId in locationIds)
            {
                if (locationStoreOption == LocationStoreOption.None)
                {
                    // Replication only sets bit if content hash is registered in Redis.
                    batch.SetBitIfExistAndRemoveIfEmptyBitMaskAsync(hash, locationId.GetContentLocationEntryBitOffset(), true).FireAndForget(context);
                }
                else
                {
                    // Always set bit because this action is batched with StringSetRangeAndBumpExpiryAsync
                    batch.StringSetBitAsync(hash, locationId.GetContentLocationEntryBitOffset(), true).FireAndForget(context);
                }
            }
        }

        private async Task<IReadOnlyList<MachineId>> GetLocationIdsForRemoteLocationsAsync(Context context, IReadOnlyList<MachineLocation> locations, CancellationToken cts)
        {
            var machineIds = new List<MachineId>(locations.Count);
            foreach (var location in locations)
            {
                machineIds.Add(await GetContentLocationIdForContentLocationAsync(context, location, cts));
            }

            return machineIds;
        }

        /// <summary>
        /// Content location data is stored in Redis in the following format :
        ///
        ///     MaxLocationId                     -> Counter for last assigned content location id
        ///     LocationId:{ID}                   -> ContentLocationData (byte[])
        ///     LocationKey:{ContentLocationHash} -> ContentLocationId (long)
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="machineLocation">Content location data</param>
        /// <param name="cts">Cancellation token</param>
        /// <returns>Content location id</returns>
        private async Task<MachineId> GetContentLocationIdForContentLocationAsync(Context context, MachineLocation machineLocation, CancellationToken cts)
        {
            ContentHash hashOfMachineLocation = machineLocation.GetContentHash(_contentHasher);

            if (!_idsByHash.TryGetValue(hashOfMachineLocation, out var machineId))
            {
                string machineLocationHash = machineLocation.GetContentHashString(_contentHasher);
                RedisValue redisValue = await _machineLocationRedisDatabaseAdapter.StringGetAsync(
                    context,
                    $"{ContentLocationKeyPrefix}{machineLocationHash}",
                    cts);

                if (redisValue != RedisValue.Null)
                {
                    // Found the machine's id
                    machineId = new MachineId((int)redisValue);
                }
                else
                {
                    // Didn't find the machine's id. Need to create it.
                    // Grab the next open machine id.
                    machineId = await CreateNewIdForMachineAsync(context, machineLocation, cts);
                    await SetMachineIdLocationMappingAsync(context, machineId, machineLocation, cts);
                }

                _idsByHash.TryAdd(hashOfMachineLocation, machineId);
            }

            return machineId;
        }

        private string GetMachineDataString(byte[] machineData)
        {
            return Convert.ToBase64String(machineData);
        }

        private string GetContentHashString(ContentHash contentHash)
        {
            return Convert.ToBase64String(contentHash.ToHashByteArray());
        }

        private ContentHash? FromContentHashString(string key)
        {
            try
            {
                byte[] hashBytes = Convert.FromBase64String(key);
                if (hashBytes.Length == _dataMigrationHashInfo.ByteLength)
                {
                    return new ContentHash(_dataMigrationHashInfo.HashType, hashBytes);
                }
            }
            catch (FormatException)
            {
            }

            return null;
        }

        /// <summary>
        ///     Poke the machine's id/location mappings to ensure they don't fall out of Redis.
        /// </summary>
        internal async Task UpdateIdentityAsync(Context context)
        {
            const UrgencyHint urgency = UrgencyHint.Minimum;

            // Lookup this machine's id
            RedisValue redisValue =
                await _machineLocationRedisDatabaseAdapter.StringGetAsync(
                    context,
                    $"{ContentLocationKeyPrefix}{_localMachineLocationHash}",
                    CancellationToken.None,
                    GetCommandFlagsForUrgencyHint(urgency));

            MachineId machineId;
            if (redisValue != RedisValue.Null)
            {
                // Found this machine's id
                machineId = new MachineId((int)redisValue);

                // Compare the id to the last value seen
                if (LocalMachineId.HasValue && LocalMachineId.Value != machineId)
                {
                    _tracer.Error(context, $"Id for local location {_localMachineLocation} changed from {LocalMachineId.Value} to {machineId}.");
                }
            }
            else
            {
                // Didn't find this machine's id. Need to create it.
                machineId = await CreateNewIdForMachineAsync(context, _localMachineLocation, CancellationToken.None);
                _tracer.Info(context, $"Created new id {machineId} for local location {_localMachineLocation}.");
            }

            // Remember the id for the next update
            LocalMachineId = machineId;

            // Set the mapping for that id to be the data for this machine
            await SetMachineIdLocationMappingAsync(context, LocalMachineId.Value, _localMachineLocation, CancellationToken.None);
        }

        private static CommandFlags GetCommandFlagsForUrgencyHint(UrgencyHint urgencyHint)
        {
            return urgencyHint < UrgencyHint.Nominal ? CommandFlags.PreferSlave : CommandFlags.None;
        }

        /// <summary>
        ///     Map the machine's location data to a new id. TODO: Move this to a static helper for re-use in a separate background task. (bug 1365340)
        /// </summary>
        private async Task<MachineId> CreateNewIdForMachineAsync(Context context, MachineLocation machineLocation, CancellationToken cts)
        {
            string machineLocationHash = machineLocation.GetContentHashString(_contentHasher);

            // Grab the next open machine id.
            long machineId = await _machineLocationRedisDatabaseAdapter.StringIncrementAsync(context, MaxContentLocationId, cts);
            _tracer.Debug(context, $"Got new location id {machineId}.");

            // Set the machine's id.
            if (await _machineLocationRedisDatabaseAdapter.StringSetAsync(
                context,
                $"{ContentLocationKeyPrefix}{machineLocationHash}",
                machineId,
                When.NotExists,
                cts))
            {
                _tracer.Debug(context, $"Set mapping from location {machineLocation} to id {machineId}.");
            }
            else
            {
                // Lost the race. Get the id set by the winner.
                RedisValue redisValue = await _machineLocationRedisDatabaseAdapter.StringGetAsync(
                    context,
                    $"{ContentLocationKeyPrefix}{machineLocationHash}",
                    cts);
                machineId = (long)redisValue;
                _tracer.Debug(context, $"Got mapping from location {machineLocation} to id {machineId}.");
            }

            return new MachineId((int)machineId);
        }

        /// <summary>
        ///     Map the id to the machine location data. TODO: Move this to a static helper for re-use in a separate background task. (bug 1365340)
        /// </summary>
        private async Task SetMachineIdLocationMappingAsync(Context context, MachineId machineId, MachineLocation machineLocation, CancellationToken cts)
        {
            if (await _machineLocationRedisDatabaseAdapter.StringSetAsync(
                context,
                $"{ContentLocationIdPrefix}{machineId.Index}",
                machineLocation.Data,
                When.NotExists,
                cts))
            {
                _tracer.Debug(context, $"Set mapping from id {machineId} to location {machineLocation}.");
            }
            else
            {
                CommandFlags command = DisableReplica ? CommandFlags.None : CommandFlags.PreferSlave;

                RedisValue newRedisValue =
                    await _machineLocationRedisDatabaseAdapter.StringGetAsync(
                        context,
                        $"{ContentLocationIdPrefix}{machineId.Index}",
                        cts,
                        command);
                byte[] foundMachineLocation = newRedisValue;
                if (!ByteArrayComparer.ArraysEqual(foundMachineLocation, machineLocation.Data))
                {
                    string message = $"Inconsistent id to location mapping for id {machineId}.";
                    _tracer.Error(context, message);
                    throw new InvalidOperationException(message);
                }
            }
        }

        /// <summary>
        /// Get expiry of content hash in Redis.
        /// </summary>
        internal Task<TimeSpan?> GetContentHashExpiryAsync(Context context, ContentHash contentHash, CancellationToken cancellationToken)
        {
            var contentHashString = GetContentHashString(contentHash);
            return _contentRedisDatabaseAdapter.GetExpiryAsync(context, contentHashString, cancellationToken);
        }

        private byte[] ConvertLongToBytes(long size)
        {
            byte[] bytes = BitConverter.GetBytes(size);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        private MachineLocation ResolveMachineLocation(MachineId machineId)
        {
            if (_locationsById.TryGetValue(machineId, out var result))
            {
                return result;
            }

            throw new InvalidOperationException($"Unable to resolve machine location for machine id '{machineId}'.");
        }

        /// <inheritdoc />
        public Task<BoolResult> InvalidateLocalMachineAsync(Context context, ILocalContentStore localStore, CancellationToken cts)
        {
            var operationContext = new OperationContext(context, cts);
            return operationContext.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var contentInfos = await localStore.GetContentInfoAsync(cts);
                    _tracer.Debug(context, $"Invalidating local mahchine. Attempting to remove {contentInfos.Count} from tracker.");
                    foreach (var page in contentInfos.GetPages(PageSize))
                    {
                        var result = await TrimBulkAsync(context, page.Select(s => s.ContentHash).ToList(), cts, UrgencyHint.Nominal);
                        if (!result)
                        {
                            return result;
                        }
                    }

                    return BoolResult.Success;
                });
        }

        /// <inheritdoc />
        public Result<IReadOnlyList<DateTime>> GetEffectiveLastAccessTimes(OperationContext context, IReadOnlyList<ContentHashWithLastAccessTime> contentHashes)
        {
            return Result.FromErrorMessage<IReadOnlyList<DateTime>>("Unexpected call to GetEffectiveLastAccessTimes on RedisContentLocationStore");
        }

        /// <inheritdoc />
        public Task<BoolResult> PutBlobAsync(OperationContext context, ContentHash hash, byte[] blob)
        {
            Contract.Assert(AreBlobsSupported, "PutBlobAsync was called and blobs are not supported.");

            return _blobAdapter.PutBlobAsync(context, hash, blob);
        }

        /// <inheritdoc />
        public Task<Result<byte[]>> GetBlobAsync(OperationContext context, ContentHash hash)
        {
            Contract.Assert(AreBlobsSupported, "GetBlobAsync was called and blobs are not supported.");

            return _blobAdapter.GetBlobAsync(context, hash);
        }

        /// <summary>
        /// Gets the page size used in bulk Redis queries.
        /// </summary>
        public virtual int PageSize => Configuration.RedisBatchPageSize;
    }
}
