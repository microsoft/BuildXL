// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
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
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using StackExchange.Redis;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    internal sealed class RedisGlobalStore : StartupShutdownSlimBase, IGlobalLocationStore, ReplicatedRedisHashKey.IReplicatedKeyHost
    {
        private const int MaxCheckpointSlotCount = 5;
        private readonly SemaphoreSlim _roleMutex = TaskUtilities.CreateMutex();

        private readonly IClock _clock;

        /// <inheritdoc />
        public ClusterState ClusterState { get; private set; }

        /// <summary>
        /// Exposing raided redis from global store for access from tests, specifically LocalLocationStoreDistributedContentTests.
        /// During test with raided redis, verifying counters are correct values.
        /// </summary>
        internal RaidedRedisDatabase RaidedRedis => _raidedRedis;

        /// <summary>
        /// The accessor for the redis database
        /// </summary>
        private readonly RaidedRedisDatabase _raidedRedis;

        /// <summary>
        /// Gets the backing primary redis database for the store
        /// </summary>
        public RedisDatabaseAdapter RedisDatabase => _raidedRedis.PrimaryRedisDb;

        private readonly ReplicatedRedisHashKey _checkpointsKey;
        private readonly ReplicatedRedisHashKey _masterLeaseKey;
        private readonly ReplicatedRedisHashKey _clusterStateKey;

        /// <summary>
        /// The fully qualified cluster state key in the database
        /// </summary>
        public RedisKey FullyQualifiedClusterStateKey => _clusterStateKey.UnsafeGetFullKey();

        internal RedisContentLocationStoreConfiguration Configuration => _configuration;
        private readonly RedisContentLocationStoreConfiguration _configuration;

        internal RedisBlobAdapter PrimaryBlobAdapter => _primaryBlobAdapter;
        private readonly RedisBlobAdapter _primaryBlobAdapter;
        internal RedisBlobAdapter SecondaryBlobAdapter => _secondaryBlobAdapter;
        private readonly RedisBlobAdapter _secondaryBlobAdapter;

        private Role? _role = null;

        /// <nodoc />
        public CounterCollection<GlobalStoreCounters> Counters { get; } = new CounterCollection<GlobalStoreCounters>();

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(RedisGlobalStore));

        /// <inheritdoc />
        Tracer ReplicatedRedisHashKey.IReplicatedKeyHost.Tracer => Tracer;

        /// <inheritdoc />
        bool ReplicatedRedisHashKey.IReplicatedKeyHost.CanMirror => _role == Role.Master && _configuration.MirrorClusterState;

        /// <inheritdoc />
        TimeSpan ReplicatedRedisHashKey.IReplicatedKeyHost.MirrorInterval => _configuration.ClusterStateMirrorInterval;

        /// <nodoc />
        public RedisGlobalStore(
            IClock clock,
            RedisContentLocationStoreConfiguration configuration,
            RedisDatabaseAdapter primaryRedisDb,
            RedisDatabaseAdapter secondaryRedisDb)
        {
            Contract.Requires(configuration.CentralStore != null);

            _clock = clock;
            _configuration = configuration;
            _raidedRedis = new RaidedRedisDatabase(Tracer, primaryRedisDb, secondaryRedisDb);
            var checkpointKeyBase = configuration.CentralStore.CentralStateKeyBase;

            _checkpointsKey = new ReplicatedRedisHashKey(configuration.GetCheckpointPrefix() + ".Checkpoints", this, _clock, _raidedRedis);
            _masterLeaseKey = new ReplicatedRedisHashKey(checkpointKeyBase + ".MasterLease", this, _clock, _raidedRedis);
            _clusterStateKey = new ReplicatedRedisHashKey(checkpointKeyBase + ".ClusterState", this, _clock, _raidedRedis);

            _primaryBlobAdapter = new RedisBlobAdapter(_raidedRedis.PrimaryRedisDb, TimeSpan.FromMinutes(_configuration.BlobExpiryTimeMinutes), _configuration.MaxBlobCapacity, _clock);
            _secondaryBlobAdapter = new RedisBlobAdapter(_raidedRedis.SecondaryRedisDb, TimeSpan.FromMinutes(_configuration.BlobExpiryTimeMinutes), _configuration.MaxBlobCapacity, _clock);
        }

        /// <inheritdoc />
        public bool AreBlobsSupported => _configuration.AreBlobsSupported;

        /// <inheritdoc />
        public CounterSet GetCounters(OperationContext context)
        {
            var counters = Counters.ToCounterSet();
            counters.Merge(_primaryBlobAdapter.GetCounters(), "PrimaryBlobAdapter.");
            counters.Merge(_secondaryBlobAdapter.GetCounters(), "SecondaryBlobAdapter.");
            counters.Merge(_raidedRedis.GetCounters(context, _role, Counters[GlobalStoreCounters.InfoStats]));
            return counters;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            List<MachineLocation> allMachineLocations = new List<MachineLocation>();
            Contract.Assert(_configuration.PrimaryMachineLocation.IsValid, "Primary machine location must be specified");

            allMachineLocations.Add(_configuration.PrimaryMachineLocation);
            allMachineLocations.AddRange(_configuration.AdditionalMachineLocations);

            var machineMappings = await Task.WhenAll(allMachineLocations.Select(machineLocation => RegisterMachineAsync(context, machineLocation)));

            ClusterState = new ClusterState(machineMappings[0].Id, machineMappings);

            return await UpdateClusterStateAsync(context, ClusterState);
        }

        public async Task<MachineMapping> RegisterMachineAsync(OperationContext context, MachineLocation machineLocation)
        {
            // Get the local machine id
            var machineIdAndIsAdded = await _clusterStateKey.UseReplicatedHashAsync(
                context,
                _configuration.RetryWindow,
                RedisOperation.StartupGetOrAddLocalMachine, 
                (batch, key) => batch.GetOrAddMachineAsync(key, machineLocation.ToString(), _clock.UtcNow))
                .ThrowIfFailureAsync();

            Tracer.Debug(context, $"Assigned machine id={machineIdAndIsAdded.machineId}, location={machineLocation}, isAdded={machineIdAndIsAdded.isAdded}.");

            return new MachineMapping(machineLocation, new MachineId(machineIdAndIsAdded.machineId));
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await ReleaseRoleIfNecessaryAsync(context);
            return BoolResult.Success;
        }

        #region Operations

        /// <inheritdoc />
        public Task<Result<IReadOnlyList<ContentLocationEntry>>> GetBulkAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var results = new ContentLocationEntry[contentHashes.Count];
                    UnixTime now = _clock.UtcNow;

                    int dualResultCount = 0;

                    foreach (var page in contentHashes.AsIndexed().GetPages(_configuration.RedisBatchPageSize))
                    {
                        var batchResult = await _raidedRedis.ExecuteRedisAsync(context, async (redisDb, token) =>
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
                                        results[indexedHash.Index] = entry;
                                        Interlocked.Increment(ref dualResultCount);
                                    }

                                    return Unit.Void;
                                });
                            }

                            // TODO ST: now this operation may fail with TaskCancelledException. But this should be traced differently!
                            return await redisDb.ExecuteBatchOperationAsync(context, redisBatch, token);

                        }, _configuration.RetryWindow);

                        if (!batchResult)
                        {
                            return new Result<IReadOnlyList<ContentLocationEntry>>(batchResult);
                        }
                    }

                    if (_raidedRedis.HasSecondary)
                    {
                        Counters[GlobalStoreCounters.GetBulkEntrySingleResult].Add(contentHashes.Count - dualResultCount);
                    }

                    return Result.Success<IReadOnlyList<ContentLocationEntry>>(results);
                },
                Counters[GlobalStoreCounters.GetBulk],
                traceErrorsOnly: true);
        }

        /// <inheritdoc />
        public Task<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ContentHashWithSize> contentHashes)
        {
            return RegisterLocationAsync(context, contentHashes, machineId);
        }

        private Task<BoolResult> RegisterLocationAsync(
            OperationContext context,
            IReadOnlyList<ContentHashWithSize> contentHashes,
            MachineId machineId,
            [CallerMemberName] string caller = null)
        {
            const int operationsPerHash = 3;
            var hashBatchSize = Math.Max(1, _configuration.RedisBatchPageSize / operationsPerHash);

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    foreach (var page in contentHashes.GetPages(hashBatchSize))
                    {
                        var batchResult = await _raidedRedis.ExecuteRedisAsync(context, async (redisDb, token) =>
                        {
                            Counters[GlobalStoreCounters.RegisterLocalLocationHashCount].Add(page.Count);

                            int requiresSetBitCount;
                            ConcurrentBitArray requiresSetBit;

                            if (_configuration.UseOptimisticRegisterLocalLocation)
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
                                        bool set = await batch.StringSetAsync(key, ContentLocationEntry.ConvertSizeAndMachineIdToRedisValue(hash.Size, machineId), _configuration.LocationEntryExpiry, When.NotExists);
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
                        }, _configuration.RetryWindow);

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

        private async Task<Unit> SetLocationBitAndExpireAsync(OperationContext context, IBatch batch, RedisKey key, ContentHashWithSize hash, MachineId machineId)
        {
            var tasks = new List<Task>();

            // NOTE: The order here matters. KeyExpire must be after creation of the entry. SetBit creates the entry if needed.
            tasks.Add(batch.StringSetBitAsync(key, machineId.GetContentLocationEntryBitOffset(), true));
            tasks.Add(batch.KeyExpireAsync(key, _configuration.LocationEntryExpiry));

            // NOTE: We don't set the size when using optimistic location registration because the entry should have already been created at this point (the prior set
            // if not exists call failed indicating the entry already exists).
            // There is a small race condition if the entry was near-expiry and this call ends up recreating the entry without the size being set. We accept
            // this possibility since we have to handle unknown size and either case and the occurrence of the race should be rare. Also, we can mitigate by
            // applying the size from the local database which should be known if the entry is that old.
            if (!_configuration.UseOptimisticRegisterLocalLocation && hash.Size >= 0)
            {
                tasks.Add(batch.StringSetRangeAsync(key, 0, ContentLocationEntry.ConvertSizeToRedisRangeBytes(hash.Size)));
            }

            await Task.WhenAll(tasks);
            return Unit.Void;
        }

        internal static string GetRedisKey(ContentHash hash)
        {
            // Use the string representation short hash used in other parts of the system (db and event stream) as the redis key
            return new ShortHash(hash).ToString();
        }

        #endregion Operations

        #region Role Management

        /// <inheritdoc />
        public async Task<Role?> ReleaseRoleIfNecessaryAsync(OperationContext context)
        {
            if (_role == Role.Master)
            {
                await context.PerformOperationAsync(
                    Tracer,
                    () => UpdateRoleAsync(context, release: true),
                    Counters[GlobalStoreCounters.ReleaseRole]).IgnoreFailure(); // Error is already observed.

                _role = null;
            }

            return _role;
        }

        private async Task<Result<Role>> UpdateRoleAsync(OperationContext context, bool release)
        {
            return await context.PerformOperationAsync<Result<Role>>(
                Tracer,
                async () =>
                {
                    // This mutex ensure that Release of master role during shutdown and Heartbeat role acquisition are synchronized.
                    // Ensuring that a heartbeat during shutdown doesn't trigger the released master role to be acquired again.
                    using (await _roleMutex.AcquireAsync())
                    {
                        if (ShutdownStarted)
                        {
                            // Don't acquire a role during shutdown
                            return Role.Worker;
                        }

                        var configuredRole = _configuration.Checkpoint?.Role;
                        if (configuredRole != null)
                        {
                            return configuredRole.Value;
                        }

                        var localMachineName = _configuration.PrimaryMachineLocation.ToString();

                        var masterAcquisitonResult = await _masterLeaseKey.UseReplicatedHashAsync(context, _configuration.RetryWindow, RedisOperation.UpdateRole, (batch, key) => batch.AcquireMasterRoleAsync(
                                masterRoleRegistryKey: key,
                                machineName: localMachineName,
                                currentTime: _clock.UtcNow,
                                leaseExpiryTime: _configuration.Checkpoint.MasterLeaseExpiryTime,
                                // 1 master only is allowed. This should be changed if more than one master becomes a possible configuration
                                slotCount: 1,
                                release: release
                            )).ThrowIfFailureAsync();

                        if (release)
                        {
                            Tracer.Debug(context, $"'{localMachineName}' released master role.");
                            return Role.Worker;
                        }

                        if (masterAcquisitonResult != null)
                        {
                            var priorMachineName = masterAcquisitonResult.Value.PriorMasterMachineName;
                            if (priorMachineName != localMachineName || masterAcquisitonResult.Value.PriorMachineStatus != SlotStatus.Acquired)
                            {
                                Tracer.Debug(context, $"'{localMachineName}' acquired master role from '{priorMachineName}', Status: '{masterAcquisitonResult?.PriorMachineStatus}', LastHeartbeat: '{masterAcquisitonResult?.PriorMasterLastHeartbeat}'");
                            }

                            return Role.Master;
                        }
                        else
                        {
                            return Role.Worker;
                        }
                    }
                },
                Counters[GlobalStoreCounters.UpdateRole]);
        }

        #endregion Role Management

        /// <inheritdoc />
        public Task<BoolResult> UpdateClusterStateAsync(OperationContext context, ClusterState clusterState)
        {
            return context.PerformOperationAsync(
                Tracer,
                () => UpdateClusterStateCoreAsync(context, clusterState),
                Counters[GlobalStoreCounters.UpdateClusterState]);
        }

        private async Task<BoolResult> UpdateClusterStateCoreAsync(OperationContext context, ClusterState clusterState)
        {
            (var inactiveMachineIdSet, var getUnknownMachinesResult) = await _clusterStateKey.UseReplicatedHashAsync(context, _configuration.RetryWindow, RedisOperation.UpdateClusterState, async (batch, key) =>
            {
                var heartbeatResultTask = CallHeartbeatAsync(context, clusterState, batch, key, MachineState.Active);

                var getUnknownMachinesTask = batch.GetUnknownMachinesAsync(
                    key,
                    clusterState.MaxMachineId);


                await Task.WhenAll(heartbeatResultTask, getUnknownMachinesTask);

                var heartbeatResult = await heartbeatResultTask;
                var getUnknownMachinesResult = await getUnknownMachinesTask;

                return (heartbeatResult, getUnknownMachinesResult);
            }).ThrowIfFailureAsync();

            if (getUnknownMachinesResult.maxMachineId != clusterState.MaxMachineId)
            {
                Tracer.Debug(context, $"Retrieved unknown machines from ({clusterState.MaxMachineId}, {getUnknownMachinesResult.maxMachineId}]");
                foreach (var item in getUnknownMachinesResult.unknownMachines)
                {
                    context.LogMachineMapping(Tracer, item.Key, item.Value);
                }
            }

            clusterState.AddUnknownMachines(getUnknownMachinesResult.maxMachineId, getUnknownMachinesResult.unknownMachines);
            clusterState.SetInactiveMachines(inactiveMachineIdSet);
            Tracer.Debug(context, $"Inactive machines: Count={inactiveMachineIdSet.Count}, [{string.Join(", ", inactiveMachineIdSet)}]");
            Tracer.TrackMetric(context, "InactiveMachineCount", inactiveMachineIdSet.Count);

            foreach (var machineMapping in clusterState.LocalMachineMappings)
            {
                if (!clusterState.TryResolveMachineId(machineMapping.Location, out var machineId))
                {
                    return new BoolResult($"Invalid redis cluster state on machine {machineMapping}. (Missing location {machineMapping.Location})");
                }
                else if (machineId != machineMapping.Id)
                {
                    Tracer.Warning(context, $"Machine id mismatch for location {machineMapping.Location}. Registered id: {machineMapping.Id}. Cluster state id: {machineId}. Updating registered id with cluster state id.");
                    machineMapping.Id = machineId;
                }

                if (getUnknownMachinesResult.maxMachineId < machineMapping.Id.Index)
                {
                    return new BoolResult($"Invalid redis cluster state on machine {machineMapping} (redis max machine id={getUnknownMachinesResult.maxMachineId})");
                }
            }

            return BoolResult.Success;
        }

        private async Task<BitMachineIdSet> CallHeartbeatAsync(
            OperationContext context,
            ClusterState clusterState,
            RedisBatch batch,
            string key,
            MachineState state)
        {
            var heartbeatResults = await Task.WhenAll(clusterState.LocalMachineMappings.Select(async machineMapping =>
            {
                (MachineState priorState, BitMachineIdSet inactiveMachineIdSet) = await batch.HeartbeatAsync(
                    key,
                    machineMapping.Id.Index,
                    state,
                    _clock.UtcNow,
                    _configuration.RecomputeInactiveMachinesExpiry,
                    _configuration.MachineExpiry);

                if (priorState != state)
                {
                    Tracer.Debug(context, $"Machine {machineMapping} state changed from {priorState} to {state}");
                }

                if (priorState == MachineState.Unavailable || priorState == MachineState.Expired)
                {
                    clusterState.LastInactiveTime = _clock.UtcNow;
                }

                return inactiveMachineIdSet;
            }).ToList());

            return heartbeatResults.FirstOrDefault() ?? BitMachineIdSet.EmptyInstance;
        }

        /// <inheritdoc />
        public Task<Result<CheckpointState>> GetCheckpointStateAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var (checkpoints, startCursor) = await _checkpointsKey.UseReplicatedHashAsync(
                        context,
                        _configuration.RetryWindow,
                        RedisOperation.GetCheckpoint,
                        (batch, key) => batch.GetCheckpointsInfoAsync(key, _clock.UtcNow))
                    .ThrowIfFailureAsync();

                    var roleResult = await UpdateRoleAsync(context, release: false);
                    if (!roleResult.Succeeded)
                    {
                        return new ErrorResult(roleResult).AsResult<Result<CheckpointState>>();
                    }

                    _role = roleResult.Value;

                    var maxCheckpoint = checkpoints.MaxByOrDefault(c => c.CheckpointCreationTime);
                    if (maxCheckpoint == null)
                    {
                        Tracer.Debug(context, $"Getting checkpoint state: Can't find a checkpoint: Start cursor time: {startCursor}");

                        // Add slack for start cursor to account for clock skew between event hub and redis
                        var epochStartCursor = startCursor - _configuration.EventStore.NewEpochEventStartCursorDelay;
                        return CheckpointState.CreateUnavailable(_role.Value, epochStartCursor);
                    }

                    Tracer.Debug(context, $"Getting checkpoint state: Found checkpoint '{maxCheckpoint}'");

                    return Result.Success(new CheckpointState(_role.Value, new EventSequencePoint(maxCheckpoint.SequenceNumber), maxCheckpoint.CheckpointId, maxCheckpoint.CheckpointCreationTime));
                },
                Counters[GlobalStoreCounters.GetCheckpointState]);
        }

        public Task<BoolResult> RegisterCheckpointAsync(OperationContext context, string checkpointId, EventSequencePoint sequencePoint)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    Contract.Assert(sequencePoint.SequenceNumber != null);

                    var checkpoint = new RedisCheckpointInfo(checkpointId, sequencePoint.SequenceNumber.Value, _clock.UtcNow, _configuration.PrimaryMachineLocation.ToString());
                    Tracer.Debug(context, $"Saving checkpoint '{checkpoint}' into the central store.");

                    var slotNumber = await _checkpointsKey.UseReplicatedHashAsync(
                        context,
                        _configuration.RetryWindow,
                        RedisOperation.UploadCheckpoint,
                        (batch, key) => batch.AddCheckpointAsync(key, checkpoint, MaxCheckpointSlotCount))
                        .ThrowIfFailureAsync();

                    Tracer.Debug(context, $"Saved checkpoint into slot '{slotNumber}'.");
                    return BoolResult.Success;
                },
                Counters[GlobalStoreCounters.RegisterCheckpoint]);
        }

        /// <inheritdoc />
        public async Task<BoolResult> InvalidateLocalMachineAsync(OperationContext context)
        {
            return await context.PerformOperationAsync(
                Tracer,
                () =>
                {
                    return _clusterStateKey.UseReplicatedHashAsync(
                        context,
                        _configuration.RetryWindow,
                        RedisOperation.InvalidateLocalMachine,
                        (batch, key) => CallHeartbeatAsync(context, ClusterState, batch, key, MachineState.Unavailable));

                }, Counters[GlobalStoreCounters.InvalidateLocalMachine]);
        }

        /// <inheritdoc />
        public Task<PutBlobResult> PutBlobAsync(OperationContext context, ContentHash hash, byte[] blob)
        {
            Contract.Assert(AreBlobsSupported, "PutBlobAsync was called and blobs are not supported.");

            return context.PerformOperationAsync(
                Tracer,
                () => GetBlobAdapter(hash).PutBlobAsync(context, hash, blob),
                traceOperationStarted: false,
                counter: Counters[GlobalStoreCounters.PutBlob]);
        }

        /// <inheritdoc />
        public Task<GetBlobResult> GetBlobAsync(OperationContext context, ContentHash hash)
        {
            Contract.Assert(AreBlobsSupported, "GetBlobAsync was called and blobs are not supported.");

            return context.PerformOperationAsync(
                Tracer,
                () => TaskUtilities.WithTimeoutAsync(_ => GetBlobAdapter(hash).GetBlobAsync(context, hash), _configuration.GetBlobTimeout, context.Token),
                traceOperationStarted: false,
                counter: Counters[GlobalStoreCounters.GetBlob]);
        }

        internal RedisBlobAdapter GetBlobAdapter(ContentHash hash)
        {
            if (!_raidedRedis.HasSecondary)
            {
                return _primaryBlobAdapter;
            }

            return hash[0] >= 128 ? _primaryBlobAdapter : _secondaryBlobAdapter;
        } 
    }
}
