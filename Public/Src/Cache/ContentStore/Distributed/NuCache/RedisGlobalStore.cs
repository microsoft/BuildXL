// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
#if MICROSOFT_INTERNAL
using Microsoft.Caching.Redis;
#else
using StackExchange.Redis;
#endif

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public sealed partial class RedisGlobalStore : StartupShutdownSlimBase, ICheckpointRegistry, IGlobalCacheStore, IClusterStateStorage, ReplicatedRedisHashKey.IReplicatedKeyHost
    {
        private const int MaxCheckpointSlotCount = 5;
        private readonly SemaphoreSlim _roleMutex = TaskUtilities.CreateMutex();

        public override bool AllowMultipleStartupAndShutdowns => true;

        private readonly IClock _clock;

        internal RaidedRedisDatabase RaidedRedis { get; }

        private readonly ReplicatedRedisHashKey _checkpointsKey;
        private readonly ReplicatedRedisHashKey _masterLeaseKey;
        private readonly ReplicatedRedisHashKey _clusterStateKey;

        /// <summary>
        /// The fully qualified cluster state key in the database
        /// </summary>
        public RedisKey FullyQualifiedClusterStateKey => _clusterStateKey.UnsafeGetFullKey();

        internal RedisContentLocationStoreConfiguration Configuration { get; }

        private RedisMemoizationAdapter MemoizationAdapter { get; }

        internal RedisBlobAdapter PrimaryBlobAdapter { get; }

        internal RedisBlobAdapter SecondaryBlobAdapter { get; }

        private MasterElectionState _lastElection = MasterElectionState.DefaultWorker;

        /// <nodoc />
        public CounterCollection<GlobalStoreCounters> Counters { get; } = new CounterCollection<GlobalStoreCounters>();

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(RedisGlobalStore));

        /// <inheritdoc />
        Tracer ReplicatedRedisHashKey.IReplicatedKeyHost.Tracer => Tracer;

        /// <inheritdoc />
        bool ReplicatedRedisHashKey.IReplicatedKeyHost.CanMirror => _masterElectionMechanism.Role == Role.Master && Configuration.MirrorClusterState;

        /// <inheritdoc />
        TimeSpan ReplicatedRedisHashKey.IReplicatedKeyHost.MirrorInterval => Configuration.ClusterStateMirrorInterval;

        private readonly IMasterElectionMechanism _masterElectionMechanism;

        /// <nodoc />
        internal RedisGlobalStore(
            IClock clock,
            RedisContentLocationStoreConfiguration configuration,
            RedisDatabaseAdapter primaryRedisDb,
            RedisDatabaseAdapter secondaryRedisDb,
            RedisDatabaseAdapter primaryRedisBlobDb,
            RedisDatabaseAdapter secondaryRedisBlobDb,
            IMasterElectionMechanism masterElectionMechanism)
        {
            Contract.Requires(configuration.CentralStore != null);

            _clock = clock;
            Configuration = configuration;
            RaidedRedis = new RaidedRedisDatabase(Tracer, primaryRedisDb, secondaryRedisDb);
            var checkpointKeyBase = configuration.CentralStore.CentralStateKeyBase;

            _checkpointsKey = new ReplicatedRedisHashKey(configuration.GetCheckpointPrefix() + ".Checkpoints", this, _clock, RaidedRedis);
            _masterLeaseKey = new ReplicatedRedisHashKey(checkpointKeyBase + ".MasterLease", this, _clock, RaidedRedis);
            _clusterStateKey = new ReplicatedRedisHashKey(checkpointKeyBase + ".ClusterState", this, _clock, RaidedRedis);

            MemoizationAdapter = new RedisMemoizationAdapter(RaidedRedis, configuration.Memoization);

            PrimaryBlobAdapter = new RedisBlobAdapter(primaryRedisBlobDb, _clock, Configuration);
            SecondaryBlobAdapter = new RedisBlobAdapter(secondaryRedisBlobDb, _clock, Configuration);

            _masterElectionMechanism = masterElectionMechanism;
        }

        /// <inheritdoc />
        public bool AreBlobsSupported => Configuration.AreBlobsSupported;

        /// <inheritdoc />
        public CounterSet GetCounters(OperationContext context)
        {
            var counters = Counters.ToCounterSet();
            counters.Merge(PrimaryBlobAdapter.GetCounters(), "PrimaryBlobAdapter.");
            counters.Merge(SecondaryBlobAdapter.GetCounters(), "SecondaryBlobAdapter.");
            counters.Merge(RaidedRedis.GetCounters(context, _masterElectionMechanism.Role, Counters[GlobalStoreCounters.InfoStats]));
            return counters;
        }

        #region Content Location Operations

        /// <inheritdoc />
        public Task<Result<IReadOnlyList<ContentLocationEntry>>> GetBulkAsync(OperationContext context, IReadOnlyList<ShortHash> contentHashes)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var results = new ContentLocationEntry[contentHashes.Count];
                    UnixTime now = _clock.UtcNow;

                    int dualResultCount = 0;

                    foreach (var page in contentHashes.AsIndexed().GetPages(Configuration.RedisBatchPageSize))
                    {
                        var batchResult = await RaidedRedis.ExecuteRedisAsync(context, async (redisDb, token) =>
                        {
                            var redisBatch = redisDb.CreateBatch(RedisOperation.GetBulkGlobal);

                            foreach (var indexedHash in page)
                            {
                                var key = GetRedisKey(indexedHash.Item);
                                redisBatch.AddOperationAndTraceIfFailure(context, key, async batch =>
                                {
                                    var redisEntry = await batch.StringGetAsync(key);
                                    ContentLocationEntry entry;
                                    if (redisEntry.IsNullOrEmpty)
                                    {
                                        entry = ContentLocationEntry.Missing;
                                    }
                                    else
                                    {
                                        entry = ContentLocationEntry.FromRedisValue(redisEntry, now, missingSizeHandling: true);
                                    }

                                    var originalEntry = Interlocked.CompareExchange(ref results[indexedHash.Index], entry, null);
                                    if (originalEntry != null)
                                    {
                                        // Existing entry was there. Merge the entries.
                                        entry = ContentLocationEntry.MergeEntries(entry, originalEntry);
                                        Interlocked.Exchange(ref results[indexedHash.Index], entry);
                                        Interlocked.Increment(ref dualResultCount);
                                    }

                                    return Unit.Void;
                                });
                            }

                            // TODO ST: now this operation may fail with TaskCancelledException. But this should be traced differently!
                            return await redisDb.ExecuteBatchOperationAsync(context, redisBatch, token);

                        }, Configuration.RetryWindow);

                        if (!batchResult)
                        {
                            return new Result<IReadOnlyList<ContentLocationEntry>>(batchResult);
                        }
                    }

                    if (RaidedRedis.HasSecondary)
                    {
                        Counters[GlobalStoreCounters.GetBulkEntrySingleResult].Add(contentHashes.Count - dualResultCount);
                    }

                    return Result.Success<IReadOnlyList<ContentLocationEntry>>(results);
                },
                Counters[GlobalStoreCounters.GetBulk],
                traceErrorsOnly: true);
        }

        /// <inheritdoc />
        public ValueTask<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> contentHashes, bool touch)
        {
            if (Configuration.DistributedContentConsumerOnly)
            {
                return BoolResult.SuccessValueTask;
            }

            return new ValueTask<BoolResult>(RegisterLocationAsync(context, contentHashes, machineId));
        }

        private Task<BoolResult> RegisterLocationAsync(
            OperationContext context,
            IReadOnlyList<ShortHashWithSize> contentHashes,
            MachineId machineId,
            [CallerMemberName] string caller = null)
        {
            const int operationsPerHash = 3;
            var hashBatchSize = Math.Max(1, Configuration.RedisBatchPageSize / operationsPerHash);

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    foreach (var page in contentHashes.GetPages(hashBatchSize))
                    {
                        var batchResult = await RaidedRedis.ExecuteRedisAsync(context, async (redisDb, token) =>
                        {
                            Counters[GlobalStoreCounters.RegisterLocalLocationHashCount].Add(page.Count);

                            int requiresSetBitCount;
                            ConcurrentBitArray requiresSetBit;

                            if (Configuration.UseOptimisticRegisterLocalLocation)
                            {
                                requiresSetBitCount = 0;
                                requiresSetBit = new ConcurrentBitArray(page.Count);
                                var redisBatch = redisDb.CreateBatch(RedisOperation.RegisterLocalSetNonExistentHashEntries);

                                // Perform initial pass to set redis entries in single operation. Fallback to more elaborate
                                // flow where we use SetBit + KeyExpire
                                foreach (var indexedHash in page.WithIndices())
                                {
                                    var hash = indexedHash.value;
                                    var key = GetRedisKey(hash.Hash);
                                    redisBatch.AddOperationAndTraceIfFailure(context, key, async batch =>
                                    {
                                        bool set = await batch.StringSetAsync(key, ContentLocationEntry.ConvertSizeAndMachineIdToRedisValue(hash.Size, machineId), Configuration.LocationEntryExpiry, When.NotExists);
                                        if (!set)
                                        {
                                            requiresSetBit[indexedHash.index] = true;
                                            Interlocked.Increment(ref requiresSetBitCount);
                                        }

                                        return set;
                                    }, operationName: "ConvertSizeAndMachineIdToRedisValue");
                                }

                                var result = await redisDb.ExecuteBatchOperationAsync(context, redisBatch, token);
                                if (!result || requiresSetBitCount == 0)
                                {
                                    return result;
                                }
                            }
                            else
                            {
                                requiresSetBitCount = page.Count;
                                requiresSetBit = null;
                            }

                            // Some keys already exist and require that we set the bit and update the expiry on the existing entry
                            using (Counters[GlobalStoreCounters.RegisterLocalLocationUpdate].Start())
                            {
                                Counters[GlobalStoreCounters.RegisterLocalLocationUpdateHashCount].Add(requiresSetBitCount);

                                var updateRedisBatch = redisDb.CreateBatch(RedisOperation.RegisterLocalSetHashEntries);

                                foreach (var hash in page.Where((h, index) => requiresSetBit?[index] ?? true))
                                {
                                    var key = GetRedisKey(hash.Hash);
                                    updateRedisBatch.AddOperationAndTraceIfFailure(
                                        context,
                                        key,
                                        batch => SetLocationBitAndExpireAsync(context, batch, key, hash, machineId),
                                        operationName: "SetLocationBitAndExpireAsync");
                                }

                                return await redisDb.ExecuteBatchOperationAsync(context, updateRedisBatch, token);
                            }
                        }, Configuration.RetryWindow);

                        if (!batchResult)
                        {
                            return batchResult;
                        }
                    }

                    return BoolResult.Success;
                },
                Counters[GlobalStoreCounters.RegisterLocalLocation],
                caller: caller,
                traceErrorsOnly: true);
        }

        private async Task<Unit> SetLocationBitAndExpireAsync(OperationContext context, IBatch batch, RedisKey key, ShortHashWithSize hash, MachineId machineId)
        {
            var tasks = new List<Task>();

            // NOTE: The order here matters. KeyExpire must be after creation of the entry. SetBit creates the entry if needed.
            tasks.Add(batch.StringSetBitAsync(key, machineId.GetContentLocationEntryBitOffset(), true));
            tasks.Add(batch.KeyExpireAsync(key, Configuration.LocationEntryExpiry));

            // NOTE: We don't set the size when using optimistic location registration because the entry should have already been created at this point (the prior set
            // if not exists call failed indicating the entry already exists).
            // There is a small race condition if the entry was near-expiry and this call ends up recreating the entry without the size being set. We accept
            // this possibility since we have to handle unknown size and either case and the occurrence of the race should be rare. Also, we can mitigate by
            // applying the size from the local database which should be known if the entry is that old.
            if (!Configuration.UseOptimisticRegisterLocalLocation && hash.Size >= 0)
            {
                tasks.Add(batch.StringSetRangeAsync(key, 0, ContentLocationEntry.ConvertSizeToRedisRangeBytes(hash.Size)));
            }

            await Task.WhenAll(tasks);
            return Unit.Void;
        }

        internal static string GetRedisKey(ShortHash hash)
        {
            // Use the string representation short hash used in other parts of the system (db and event stream) as the redis key
            // ShortHash.ToString had a bug when only 10 bytes of the hash were printed.
            // Even though the bug is fixed, this method should return the same (old, i.e. shorter) representation
            // to avoid braking the world after the new version is deployed.
            return hash.ToString(ShortHash.HashLength - 1);
        }

        #endregion Operations

        #region Cluster State Management

        public Task<Result<MachineMapping>> RegisterMachineAsync(OperationContext context, MachineLocation machineLocation)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                if (Configuration.DistributedContentConsumerOnly)
                {
                    return Result.Success(new MachineMapping(machineLocation, new MachineId(0)));
                }

                // Get the local machine id
                var machineIdAndIsAdded = await _clusterStateKey.UseNonConcurrentReplicatedHashAsync(
                    context,
                    Configuration.RetryWindow,
                    RedisOperation.StartupGetOrAddLocalMachine,
                    (batch, key) => batch.GetOrAddMachineAsync(key, machineLocation.ToString(), _clock.UtcNow),
                    timeout: Configuration.ClusterRedisOperationTimeout)
                    .ThrowIfFailureAsync();

                Tracer.Debug(context, $"Assigned machine id={machineIdAndIsAdded.machineId}, location={machineLocation}, isAdded={machineIdAndIsAdded.isAdded}.");

                return Result.Success(new MachineMapping(machineLocation, new MachineId(machineIdAndIsAdded.machineId)));
            },
            traceOperationStarted: false,
            extraEndMessage: r =>
            {
                if (r.Succeeded)
                {
                    return $"MachineLocation=[{r.Value.Location}] MachineId=[{r.Value.Id}]";
                }
                else
                {
                    return $"MachineLocation=[{machineLocation}]";
                }
            });
        }

        public Task<Result<HeartbeatMachineResponse>> HeartbeatAsync(OperationContext context, HeartbeatMachineRequest request)
        {
            return context.PerformOperationAsync(
                Tracer,
                () =>
                {
                    return _clusterStateKey.UseNonConcurrentReplicatedHashAsync(
                        context, Configuration.RetryWindow, RedisOperation.UpdateClusterState, async (batch, key) =>
                        {
                            (MachineState priorState, BitMachineIdSet inactiveMachineIdSet, BitMachineIdSet closedMachineIdSet) = await batch.HeartbeatAsync(
                                key,
                                request.MachineId.Index,
                                // When readonly, specify Unknown which does not update state
                                Configuration.DistributedContentConsumerOnly ? MachineState.Unknown : request.DeclaredMachineState,
                                _clock.UtcNow,
                                Configuration.MachineStateRecomputeInterval,
                                Configuration.MachineActiveToClosedInterval,
                                Configuration.MachineActiveToExpiredInterval);

                            return Result.Success(new HeartbeatMachineResponse()
                            {
                                PriorState = priorState,
                                InactiveMachines = inactiveMachineIdSet,
                                ClosedMachines = closedMachineIdSet
                            });
                        },
                        timeout: Configuration.ClusterRedisOperationTimeout).ThrowIfFailureAsync();

                },
                Counters[GlobalStoreCounters.UpdateClusterState]);
        }

        public Task<Result<GetClusterUpdatesResponse>> GetClusterUpdatesAsync(OperationContext context, GetClusterUpdatesRequest request)
        {
            return context.PerformOperationAsync(
                Tracer,
                () =>
                {
                    return _clusterStateKey.UseNonConcurrentReplicatedHashAsync(
                        context, Configuration.RetryWindow, RedisOperation.UpdateClusterState, async (batch, key) =>
                        {
                            var getUnknownMachinesResult = await batch.GetUnknownMachinesAsync(
                                key,
                                request.MaxMachineId);

                            return Result.Success(new GetClusterUpdatesResponse()
                            {
                                UnknownMachines = getUnknownMachinesResult.unknownMachines,
                                MaxMachineId = getUnknownMachinesResult.maxMachineId
                            });
                        },
                        timeout: Configuration.ClusterRedisOperationTimeout).ThrowIfFailureAsync();
                },
                Counters[GlobalStoreCounters.UpdateClusterState]);
        }

        #endregion

        #region Checkpoint Registry

        /// <inheritdoc />
        public Task<Result<CheckpointState>> GetCheckpointStateAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var (checkpoints, startCursor) = await _checkpointsKey.UseNonConcurrentReplicatedHashAsync(
                        context,
                        Configuration.RetryWindow,
                        RedisOperation.GetCheckpoint,
                        (batch, key) => batch.GetCheckpointsInfoAsync(key, _clock.UtcNow),
                        timeout: Configuration.ClusterRedisOperationTimeout)
                    .ThrowIfFailureAsync();

                    var maxCheckpoint = checkpoints.MaxByOrDefault(c => c.CheckpointCreationTime);
                    if (maxCheckpoint == null)
                    {
                        Tracer.Debug(context, $"Getting checkpoint state: Can't find a checkpoint: Start cursor time: {startCursor}");

                        // Add slack for start cursor to account for clock skew between event hub and redis
                        var epochStartCursor = startCursor - Configuration.EventStore.NewEpochEventStartCursorDelay;
                        return CheckpointState.CreateUnavailable(epochStartCursor);
                    }

                    Tracer.Debug(context, $"Getting checkpoint state: Found checkpoint '{maxCheckpoint}'");

                    return Result.Success(new CheckpointState(new EventSequencePoint(maxCheckpoint.SequenceNumber), maxCheckpoint.CheckpointId, maxCheckpoint.CheckpointCreationTime, new MachineLocation(maxCheckpoint.MachineName)));
                },
                Counters[GlobalStoreCounters.GetCheckpointState]);
        }

        public Task<BoolResult> RegisterCheckpointAsync(OperationContext context, CheckpointState state)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async nestedContext =>
                {
                    Contract.Assert(state.CheckpointId != null);
                    Contract.Assert(state.StartSequencePoint.SequenceNumber != null);
                    Contract.Assert(state.Producer.IsValid);

                    var checkpoint = new RedisCheckpointInfo(state.CheckpointId, state.StartSequencePoint.SequenceNumber.Value, state.CheckpointTime, state.Producer.ToString());
                    Tracer.Debug(nestedContext, $"Saving checkpoint '{checkpoint}' into the central store.");

                    var slotNumber = await _checkpointsKey.UseNonConcurrentReplicatedHashAsync(
                        nestedContext,
                        Configuration.RetryWindow,
                        RedisOperation.UploadCheckpoint,
                        (batch, key) => batch.AddCheckpointAsync(key, checkpoint, MaxCheckpointSlotCount),
                        timeout: Configuration.ClusterRedisOperationTimeout)
                        .ThrowIfFailureAsync();

                    Tracer.Debug(nestedContext, $"Saved checkpoint into slot '{slotNumber}'.");
                    return BoolResult.Success;
                },
                timeout: Configuration.ClusterRedisOperationTimeout,
                Counters[GlobalStoreCounters.RegisterCheckpoint]);
        }

        public Task<BoolResult> ClearCheckpointsAsync(OperationContext context)
        {
            throw new NotImplementedException("This method should never be called");
        }

        #endregion

        #region Blobs in Redis

        /// <inheritdoc />
        public Task<PutBlobResult> PutBlobAsync(OperationContext context, ShortHash hash, byte[] blob)
        {
            Contract.Assert(AreBlobsSupported, "PutBlobAsync was called and blobs are not supported.");

            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                nestedContext => GetBlobAdapter(hash).PutBlobAsync(nestedContext, hash, blob),
                traceOperationStarted: false,
                counter: Counters[GlobalStoreCounters.PutBlob],
                timeout: Configuration.BlobTimeout);
        }

        /// <inheritdoc />
        public Task<GetBlobResult> GetBlobAsync(OperationContext context, ShortHash hash)
        {
            Contract.Assert(AreBlobsSupported, "GetBlobAsync was called and blobs are not supported.");

            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                nestedContext => GetBlobAdapter(hash).GetBlobAsync(nestedContext, hash),
                traceOperationStarted: false,
                counter: Counters[GlobalStoreCounters.GetBlob],
                timeout: Configuration.BlobTimeout);
        }

        internal RedisBlobAdapter GetBlobAdapter(ShortHash hash)
        {
            if (!RaidedRedis.HasSecondary)
            {
                return PrimaryBlobAdapter;
            }

            return hash[0] >= 128 ? PrimaryBlobAdapter : SecondaryBlobAdapter;
        }

        #endregion

        #region Metadata Operations

        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return MemoizationAdapter.GetLevelSelectorsAsync(context, weakFingerprint, level);
        }

        public Task<Result<bool>> CompareExchangeAsync(OperationContext context, StrongFingerprint strongFingerprint, SerializedMetadataEntry replacement, string expectedReplacementToken)
        {
            return MemoizationAdapter.CompareExchangeAsync(context, strongFingerprint, replacement, expectedReplacementToken);
        }

        public Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            return MemoizationAdapter.GetContentHashListAsync(context, strongFingerprint);
        }

        #endregion
    }
}
