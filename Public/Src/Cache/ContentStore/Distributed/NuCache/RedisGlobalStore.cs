// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    internal sealed class RedisGlobalStore : StartupShutdownSlimBase, IGlobalLocationStore
    {
        private const int MaxCheckpointSlotCount = 5;
        private readonly SemaphoreSlim _roleMutex = TaskUtilities.CreateMutex();
        private static readonly Task<HashEntry[]> _emptyClusterStateDump = Task.FromResult<HashEntry[]>(CollectionUtilities.EmptyArray<HashEntry>());

        private readonly IClock _clock;

        /// <summary>
        /// Primary redis instance used for cluster state and locations
        /// </summary>
        private readonly RedisDatabaseAdapter _primaryRedisDb;

        /// <summary>
        /// Secondary redis instance used to store backup of locations. NOT used for cluster state because
        /// reconciling these two is non-trivial and data loss does not typically occur with cluster state.
        /// </summary>
        private readonly RedisDatabaseAdapter _secondaryRedisDb;

        private bool HasSecondary => _secondaryRedisDb != null;

        private readonly string _checkpointsKey;
        private readonly string _masterLeaseKey;

        private readonly string _clusterStateKey;

        /// <summary>
        /// The fully qualified cluster state key in the database
        /// </summary>
        public RedisKey FullyQualifiedClusterStateKey => _primaryRedisDb.KeySpace + _clusterStateKey;

        private readonly RedisContentLocationStoreConfiguration _configuration;

        private readonly RedisBlobAdapter _blobAdapter;

        /// <nodoc />
        public MachineId LocalMachineId { get; private set; }

        /// <inheritdoc />
        public MachineLocation LocalMachineLocation { get; }

        private Role? _role = null;
        private DateTime _lastClusterStateMirrorTime = DateTime.MinValue;

        /// <nodoc />
        public CounterCollection<GlobalStoreCounters> Counters { get; } = new CounterCollection<GlobalStoreCounters>();

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(RedisGlobalStore));

        /// <nodoc />
        public RedisGlobalStore(IClock clock, RedisContentLocationStoreConfiguration configuration, MachineLocation localMachineLocation, RedisDatabaseAdapter primaryRedisDb, RedisDatabaseAdapter secondaryRedisDb)
        {
            Contract.Requires(configuration.CentralStore != null);

            _clock = clock;
            _configuration = configuration;
            _primaryRedisDb = primaryRedisDb;
            _secondaryRedisDb = secondaryRedisDb;
            var checkpointKeyBase = configuration.CentralStore.CentralStateKeyBase;

            _checkpointsKey = configuration.GetCheckpointPrefix() + ".Checkpoints";
            _masterLeaseKey = checkpointKeyBase + ".MasterLease";
            _clusterStateKey = checkpointKeyBase + ".ClusterState";
            LocalMachineLocation = localMachineLocation;

            _blobAdapter = new RedisBlobAdapter(_primaryRedisDb, TimeSpan.FromMinutes(_configuration.BlobExpiryTimeMinutes), _configuration.MaxBlobCapacity, _clock, Tracer);
        }

        /// <inheritdoc />
        public bool AreBlobsSupported => _configuration.AreBlobsSupported;

        private async Task<BoolResult> ExecuteRedisAsync(OperationContext context, Func<RedisDatabaseAdapter, Task<BoolResult>> executeAsync, [CallerMemberName]string caller = null)
        {
            Task<BoolResult> primaryResultTask = ExecuteAndCaptureRedisErrorsAsync(_primaryRedisDb, executeAsync);

            Task<BoolResult> secondaryResultTask = BoolResult.SuccessTask;
            if (HasSecondary)
            {
                secondaryResultTask = ExecuteAndCaptureRedisErrorsAsync(_secondaryRedisDb, executeAsync);
            }

            await Task.WhenAll(primaryResultTask, secondaryResultTask);

            var primaryResult = await primaryResultTask;
            if (_secondaryRedisDb == null)
            {
                return primaryResult;
            }

            var secondaryResult = await secondaryResultTask;

            if (primaryResult.Succeeded != secondaryResult.Succeeded)
            {
                var failingRedisDb = GetDbName(primaryResult.Succeeded ? _secondaryRedisDb : _primaryRedisDb);
                Tracer.Info(context, $"{Tracer.Name}.{caller}: Error in {failingRedisDb} redis db using result from other redis db: {primaryResult & secondaryResult}");
            }

            return primaryResult | secondaryResult;
        }

        private async Task<TResult> ExecuteRedisFallbackAsync<TResult>(OperationContext context, Func<RedisDatabaseAdapter, Task<TResult>> executeAsync, [CallerMemberName]string caller = null)
            where TResult : ResultBase
        {
            var primaryResult = await ExecuteAndCaptureRedisErrorsAsync(_primaryRedisDb, executeAsync);
            if (!primaryResult.Succeeded && HasSecondary)
            {
                Tracer.Info(context, $"{Tracer.Name}.{caller}: Error in {GetDbName(_primaryRedisDb)} redis db falling back to secondary redis db: {primaryResult}");
                return await ExecuteAndCaptureRedisErrorsAsync(_secondaryRedisDb, executeAsync);
            }

            return primaryResult;
        }

        private string GetDbName(RedisDatabaseAdapter redisDb)
        {
            return redisDb == _primaryRedisDb ? "primary" : "secondary";
        }

        private bool IsPrimary(RedisDatabaseAdapter redisDb)
        {
            return redisDb == _primaryRedisDb;
        }

        private async Task<TResult> ExecuteAndCaptureRedisErrorsAsync<TResult>(RedisDatabaseAdapter redisDb, Func<RedisDatabaseAdapter, Task<TResult>> executeAsync)
            where TResult : ResultBase
        {
            try
            {
                return await executeAsync(redisDb);
            }
            catch (RedisConnectionException ex)
            {
                return new ErrorResult(ex).AsResult<TResult>();
            }
        }

        /// <inheritdoc />
        public CounterSet GetCounters(OperationContext context)
        {
            var counters = Counters.ToCounterSet();
            counters.Merge(_blobAdapter.GetCounters(), "BlobAdapter.");
            counters.Merge(_primaryRedisDb.Counters.ToCounterSet(), "Redis.");

            if (_role != Role.Worker)
            {
                // Don't print redis counters on workers
                counters.Merge(_primaryRedisDb.GetRedisCounters(context, Tracer, Counters[GlobalStoreCounters.InfoStats]), "RedisInfo.");
            }

            if (HasSecondary)
            {
                counters.Merge(_secondaryRedisDb.Counters.ToCounterSet(), "SecondaryRedis.");

                if (_role != Role.Worker)
                {
                    // Don't print redis counters on workers
                    counters.Merge(_secondaryRedisDb.GetRedisCounters(context, Tracer, Counters[GlobalStoreCounters.InfoStats]), "SecondaryRedisInfo.");
                }
            }

            return counters;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var machineLocation = LocalMachineLocation;

            var machineId = await RegisterMachineAsync(context, machineLocation);
            LocalMachineId = new MachineId(machineId);

            Tracer.Info(context, $"Secondary redis enabled={HasSecondary}");

            return BoolResult.Success;
        }

        internal async Task<int> RegisterMachineAsync(OperationContext context, MachineLocation machineLocation)
        {
            // Get the local machine id
            return await ExecuteRedisFallbackAsync(context, redisDb => redisDb.ExecuteBatchAsync(context, async batch =>
            {
                var machineIdAndIsAdded = await batch.GetOrAddMachineAsync(_clusterStateKey, machineLocation.ToString(), _clock.UtcNow);

                Tracer.Debug(context, $"Assigned machine id={machineIdAndIsAdded.machineId}, location={machineLocation}, isAdded={machineIdAndIsAdded.isAdded}.");

                return Result.Success(machineIdAndIsAdded.machineId);
            },
            RedisOperation.StartupGetOrAddLocalMachine)).ThrowIfFailureAsync();
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
                        var batchResult = await ExecuteRedisAsync(context, async redisDb =>
                        {
                            var redisBatch = redisDb.CreateBatch(RedisOperation.GetBulkGlobal);

                            foreach (var indexedHash in page)
                            {
                                var key = GetRedisKey(indexedHash.Item);
                                redisBatch.AddOperation(key, async batch =>
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
                                        entry = MergeEntries(entry, originalEntry);
                                        results[indexedHash.Index] = entry;
                                        Interlocked.Increment(ref dualResultCount);
                                    }

                                    return Unit.Void;
                                }).FireAndForget(context);
                            }

                            return await redisDb.ExecuteBatchOperationAsync(context, redisBatch, context.Token);

                        });

                        if (!batchResult)
                        {
                            return new Result<IReadOnlyList<ContentLocationEntry>>(batchResult);
                        }
                    }

                    if (HasSecondary)
                    {
                        Counters[GlobalStoreCounters.GetBulkEntrySingleResult].Add(contentHashes.Count - dualResultCount);
                    }

                    return Result.Success<IReadOnlyList<ContentLocationEntry>>(results);
                },
                Counters[GlobalStoreCounters.GetBulk]);
        }

        private ContentLocationEntry MergeEntries(ContentLocationEntry entry, ContentLocationEntry originalEntry)
        {
            if (entry.IsMissing)
            {
                return originalEntry;
            }

            if (originalEntry.IsMissing)
            {
                return entry;
            }

            return entry.SetMachineExistence(originalEntry.Locations, exists: true); ;
        }

        /// <inheritdoc />
        public Task<BoolResult> RegisterLocalLocationAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentHashes)
        {
            return RegisterLocationAsync(context, contentHashes, LocalMachineId);
        }

        /// <inheritdoc />
        internal Task<BoolResult> RegisterLocationByIdAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentHashes, int id)
        {
            return RegisterLocationAsync(context, contentHashes, new MachineId(id));
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
                        var batchResult = await ExecuteRedisAsync(context, async redisDb =>
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
                                    redisBatch.AddOperation(key, async batch =>
                                    {
                                        bool set = await batch.StringSetAsync(key, ContentLocationEntry.ConvertSizeAndMachineIdToRedisValue(hash.Size, machineId), _configuration.LocationEntryExpiry, When.NotExists);
                                        if (!set)
                                        {
                                            requiresSetBit[indexedHash.index] = true;
                                            Interlocked.Increment(ref requiresSetBitCount);
                                        }

                                        return set;
                                    }).FireAndForget(context);
                                }

                                var result = await redisDb.ExecuteBatchOperationAsync(context, redisBatch, context.Token);
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
                                    updateRedisBatch.AddOperation(key, batch => SetLocationBitAndExpireAsync(context, batch, key, hash, machineId)).FireAndForget(context);
                                }

                                return await redisDb.ExecuteBatchOperationAsync(context, updateRedisBatch, context.Token);
                            }
                        });

                        if (!batchResult)
                        {
                            return batchResult;
                        }
                    }

                    return BoolResult.Success;
                },
                Counters[GlobalStoreCounters.RegisterLocalLocation],
                caller: caller,
                traceOperationStarted: false);
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

                        var localMachineName = LocalMachineLocation.ToString();

                        var masterAcquisitonResult = await _primaryRedisDb.ExecuteBatchAsync(context, batch => batch.AcquireMasterRoleAsync(
                                masterRoleRegistryKey: _masterLeaseKey,
                                machineName: localMachineName,
                                currentTime: _clock.UtcNow,
                                leaseExpiryTime: _configuration.Checkpoint.MasterLeaseExpiryTime,
                                // 1 master only is allowed. This should be changed if more than one master becomes a possible configuration
                                slotCount: 1,
                                release: release
                            ), RedisOperation.UpdateRole);

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
                Counters[GlobalStoreCounters.UpdateRole]).FireAndForgetAndReturnTask(context);
        }

        #endregion Role Management

        /// <inheritdoc />
        public Task<BoolResult> UpdateClusterStateAsync(OperationContext context, ClusterState clusterState)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    HashEntry[] clusterStateDump = await ExecuteRedisFallbackAsync(context, redisDb => UpdateLocalClusterStateAsync(context, clusterState, redisDb)).ThrowIfFailureAsync();

                    if (clusterStateDump.Length != 0 && HasSecondary && _configuration.MirrorClusterState)
                    {
                        Tracer.Debug(context, $"Mirroring cluster state with '{clusterStateDump.Length}' entries to secondary");
                        await _secondaryRedisDb.ExecuteBatchAsync(context, batch => batch.AddOperation(_clusterStateKey, async b =>
                        {
                            await b.HashSetAsync(_clusterStateKey, clusterStateDump);
                            return Unit.Void;
                        }),
                        RedisOperation.MirrorClusterState).FireAndForgetErrorsAsync(context);
                    }

                    return BoolResult.Success;
                }, Counters[GlobalStoreCounters.UpdateClusterState]);
        }

        private Task<Result<HashEntry[]>> UpdateLocalClusterStateAsync(OperationContext context, ClusterState clusterState, RedisDatabaseAdapter redisDb)
        {
            return redisDb.ExecuteBatchAsync(context, async batch =>
            {
                var heartbeatResultTask = CallHeartbeatAsync(context, batch, MachineState.Active);
                var getUnknownMachinesTask = batch.GetUnknownMachinesAsync(
                    _clusterStateKey,
                    clusterState.MaxMachineId);

                // Only master should mirror cluster state
                bool shouldMirrorClusterState = _role == Role.Master
                    && HasSecondary
                    && _configuration.MirrorClusterState
                    // Only mirror after a long interval, but not long enough to allow machines to appear expired
                    && !_lastClusterStateMirrorTime.IsRecent(_clock.UtcNow, _configuration.ClusterStateMirrorInterval)
                    // Only mirror from primary to secondary, so no need to dump cluster state if this is the secondary
                    && IsPrimary(redisDb);

                Task<HashEntry[]> dumpClusterStateBlobTask = shouldMirrorClusterState
                    ? batch.AddOperation(_clusterStateKey, b => b.HashGetAllAsync(_clusterStateKey))
                    : _emptyClusterStateDump;

                await Task.WhenAll(heartbeatResultTask, getUnknownMachinesTask, dumpClusterStateBlobTask);

                var clusterStateBlob = await dumpClusterStateBlobTask ?? CollectionUtilities.EmptyArray<HashEntry>();
                var heartbeatResult = await heartbeatResultTask;
                var getUnknownMachinesResult = await getUnknownMachinesTask;

                if (shouldMirrorClusterState)
                {
                    _lastClusterStateMirrorTime = _clock.UtcNow;
                }

                if (getUnknownMachinesResult.maxMachineId < LocalMachineId.Index)
                {
                    return Result.FromErrorMessage<HashEntry[]>($"Invalid {GetDbName(redisDb)} redis cluster state on machine {LocalMachineId} (max machine id={getUnknownMachinesResult.maxMachineId})");
                }

                if (heartbeatResult.priorState == MachineState.Unavailable || heartbeatResult.priorState == MachineState.Expired)
                {
                    clusterState.LastInactiveTime = _clock.UtcNow;
                }

                if (getUnknownMachinesResult.maxMachineId != clusterState.MaxMachineId)
                {
                    Tracer.Debug(context, $"Retrieved unknown machines from ({clusterState.MaxMachineId}, {getUnknownMachinesResult.maxMachineId}]");
                    foreach (var item in getUnknownMachinesResult.unknownMachines)
                    {
                        context.LogMachineMapping(Tracer, item.Key, item.Value);
                    }
                }

                clusterState.AddUnknownMachines(getUnknownMachinesResult.maxMachineId, getUnknownMachinesResult.unknownMachines);
                clusterState.SetInactiveMachines(heartbeatResult.inactiveMachineIdSet);
                Tracer.Debug(context, $"Inactive machines: Count={heartbeatResult.inactiveMachineIdSet.Count}, [{string.Join(", ", heartbeatResult.inactiveMachineIdSet)}]");
                Tracer.TrackMetric(context, "InactiveMachineCount", heartbeatResult.inactiveMachineIdSet.Count);

                return Result.Success(await dumpClusterStateBlobTask ?? CollectionUtilities.EmptyArray<HashEntry>());
            },
            RedisOperation.UpdateClusterState);
        }

        private async Task<(MachineState priorState, BitMachineIdSet inactiveMachineIdSet)> CallHeartbeatAsync(OperationContext context, RedisBatch batch, MachineState state)
        {
            var heartbeatResult = await batch.HeartbeatAsync(
                _clusterStateKey,
                LocalMachineId.Index,
                state,
                _clock.UtcNow,
                _configuration.RecomputeInactiveMachinesExpiry,
                _configuration.MachineExpiry);

            if (heartbeatResult.priorState != state)
            {
                Tracer.Debug(context, $"Machine state changed from {heartbeatResult.priorState} to {state}");
            }

            return heartbeatResult;
        }

        /// <inheritdoc />
        public Task<Result<CheckpointState>> GetCheckpointStateAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var (checkpoints, startCursor) = await ExecuteRedisFallbackAsync(context, async redisDb =>
                        Result.Success(await redisDb.ExecuteBatchAsync(context, batch => batch.GetCheckpointsInfoAsync(_checkpointsKey, _clock.UtcNow), RedisOperation.GetCheckpoint)))
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
                () =>
                {
                    Contract.Assert(sequencePoint.SequenceNumber != null);

                    var checkpoint = new RedisCheckpointInfo(checkpointId, sequencePoint.SequenceNumber.Value, _clock.UtcNow, LocalMachineLocation.ToString());
                    Tracer.Debug(context, $"Saving checkpoint '{checkpoint}' into the central store.");

                    return ExecuteRedisAsync(context, async redisDb =>
                    {
                        var slotNumber = await redisDb.ExecuteBatchAsync(context, batch =>
                        {
                            return batch.AddCheckpointAsync(_checkpointsKey, checkpoint, MaxCheckpointSlotCount);
                        }, RedisOperation.UploadCheckpoint);

                        Tracer.Debug(context, $"Saved checkpoint into slot '{slotNumber}' on {GetDbName(redisDb)}.");
                        return BoolResult.Success;
                    });
                },
                Counters[GlobalStoreCounters.RegisterCheckpoint]);
        }

        /// <inheritdoc />
        public Task<BoolResult> InvalidateLocalMachineAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                () =>
                {
                    return ExecuteRedisAsync(context, async redisDb =>
                    {
                        await redisDb.ExecuteBatchAsync(context, batch => CallHeartbeatAsync(context, batch, MachineState.Unavailable), RedisOperation.InvalidateLocalMachine);
                        return BoolResult.Success;
                    });

                }, Counters[GlobalStoreCounters.InvalidateLocalMachine]);
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
    }
}
