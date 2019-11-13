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
    /// <summary>
    /// Defines a key for a Redis hash (hash table in redis) where the key is periodically replicated to secondary instance and can be recovered from secondary instance.
    /// 
    /// This component ensures that in the event of redis failures or data loss from one of two raided redis instances
    /// that operations can still succeed and function properly.
    /// 
    /// Strategy:
    /// The redis hash is assigned a field 'ReplicatedHashVersionNumber' which is a monotonically increasing integral value. Each successful access to the redis hash updates this value.
    /// The result of the redis instance with the highest value for this number is preferred (or the primary if they are equal for the instances). Periodically the master will update replicate 
    /// the value from the preferred instance to the less preferred instance (i.e. restore to primary or backup to secondary). In the event of a successful restore to primary, the secondary 
    /// version number will be reset to its prior value so that the primary becomes the preferred instance again.
    /// 
    /// Note: There are still some conditions where failures are possible and this component does not attempt to resolve all race
    /// conditions that can happen in the event of failures. The assumption is that the data updated by this component is rarely updated
    /// in a way that all operations must be successful for the data to remain valid. This is true for the current set of keys managed by this
    /// component:
    /// ClusterStateKey - this is an important key. Most update operations simply update the machines last heartbeat time. These can be lost for small periods of time with
    /// no ill effects. 
    ///     WARNING: Failures during new machine addition are not always handled properly by this strategy may result in recurring failures. New machine addition is very rare so
    ///     the assumption that the likelihood of a failure during new machine addition will be essentially non-existent.
    /// 
    /// MasterLeaseKey - operations here are infrequent and transient failures would only mean cluster does not properly assign a master for a short period of time.
    /// 
    /// CheckpointsKey - most operations are reads. Transient update failures will simply mean the checkpoint is not updated so machines may end up going back to
    /// slightly older point in time
    /// </summary>
    internal sealed class ReplicatedRedisHashKey
    {
        /// <summary>
        /// Name of field in redis hash which indicates the version number of key. 
        /// </summary>
        private const string ReplicatedHashVersionNumber = nameof(ReplicatedHashVersionNumber);

        private readonly string _key;
        private readonly IReplicatedKeyHost _host;
        private readonly IClock _clock;
        private readonly RaidedRedisDatabase _redis;

        private DateTime _lastMirrorTime;

        /// <nodoc />
        public ReplicatedRedisHashKey(string key, IReplicatedKeyHost host, IClock clock, RaidedRedisDatabase redis)
        {
            _clock = clock;
            _redis = redis;
            _key = key;
            _host = host;
        }

        /// <summary>
        /// Perform an operation against the redis hash using logic to ensure that result of operation comes from instance with latest updates and is resilient to data loss in one of the instances
        /// </summary>
        public async Task<Result<T>> UseReplicatedHashAsync<T>(OperationContext context, RedisOperation operation, Func<RedisBatch, string, Task<T>> addOperations, [CallerMemberName] string caller = null)
        {
            // Query to see which db has highest version number
            (var primaryVersionedResult, var secondaryVersionedResult) = await _redis.ExecuteRaidedAsync(context, async redisDb =>
            {
                var operationResult = await redisDb.ExecuteBatchAsync(context,
                    async batch =>
                    {
                        var versionTask = batch.AddOperation(_key, b => b.HashIncrementAsync(_key, nameof(ReplicatedHashVersionNumber)));
                        var result = await addOperations(batch, _key);
                        var version = await versionTask;
                        return (result, version);
                    },
                    operation);

                return Result.Success(operationResult);
            },

            // Always run on primary first to ensure that primary version will always be greater or equal in cases of
            // concurrent writers to the key in normal case where primary and secondary are both updated successfully
            concurrent: false,
            caller: caller);

            if (!_redis.HasSecondary || !secondaryVersionedResult.Succeeded)
            {
                // No secondary or error in secondary, just use primary result
                return new Result<T>(primaryVersionedResult.Value.result, isNullAllowed: true);
            }

            if (!primaryVersionedResult.Succeeded)
            {
                // Error in primary, just use secondary
                return new Result<T>(secondaryVersionedResult.Value.result, isNullAllowed: true);
            }

            // Prefer db with highest version number (or primary if equal)
            bool preferPrimary = primaryVersionedResult.Value.version >= secondaryVersionedResult.Value.version;

            if (_host.CanMirror && !_lastMirrorTime.IsRecent(_clock.UtcNow, _host.MirrorInterval))
            {
                _lastMirrorTime = _clock.UtcNow;

                if (preferPrimary)
                {
                    // Primary is preferred, try to backup to secondary
                    await TryMirrorRedisHashDataAsync(
                        context,
                        source: _redis.PrimaryRedisDb,
                        target: _redis.SecondaryRedisDb);
                }
                else
                {
                    await TryMirrorRedisHashDataAsync(
                        context,
                        source: _redis.SecondaryRedisDb,
                        target: _redis.PrimaryRedisDb,

                        // Set version of secondary db to its prior version so primary will now take precedence
                        // Primary will have the current version of secondary after mirroring and secondary will have 
                        postMirrorSourceVersion: secondaryVersionedResult.Value.version);
                }
            }

            var result = preferPrimary ? primaryVersionedResult.Value.result : secondaryVersionedResult.Value.result;
            return new Result<T>(result, isNullAllowed: true);
        }

        private async Task TryMirrorRedisHashDataAsync(OperationContext context, RedisDatabaseAdapter source, RedisDatabaseAdapter target, long? postMirrorSourceVersion = null)
        {
            await context.PerformOperationAsync(
                _host.Tracer,
                async () =>
                {
                    var sourceDump = await source.ExecuteBatchAsync(context,
                        b => b.AddOperation(_key, b => b.KeyDumpAsync(_key)),
                        RedisOperation.HashGetKeys);

                    await target.ExecuteBatchAsync(context,
                        b =>
                        {
                            var deleteTask = b.AddOperation(_key, b => b.KeyDeleteAsync(_key));
                            var restoreTask = b.AddOperation(_key, b => b.KeyRestoreAsync(_key, sourceDump).WithResultAsync(Unit.Void));
                            return Task.WhenAll(deleteTask, restoreTask).WithResultAsync(Unit.Void);
                        },
                        RedisOperation.HashDeleteAndRestore);

                    if (postMirrorSourceVersion.HasValue)
                    {
                        await source.ExecuteBatchAsync(context,
                            b => b.AddOperation(_key, b => b.HashSetAsync(_key, nameof(ReplicatedHashVersionNumber), postMirrorSourceVersion.Value)),
                            RedisOperation.HashSetValue);
                    }

                    return Result.Success(sourceDump.Length);
                },
                extraStartMessage: $"({_redis.GetDbName(source)} -> {_redis.GetDbName(target)}) Key={_key}, PostMirrorSourceVersion={postMirrorSourceVersion ?? -1L}",
                extraEndMessage: r => $"({_redis.GetDbName(source)} -> {_redis.GetDbName(target)}) Key={_key}, Length={r.GetValueOrDefault(-1)}").IgnoreFailure();
        }

        /// <summary>
        /// Exposes full redis key with keyspace.
        /// NOTE: This is only available for testing purpopses
        /// </summary>
        public RedisKey UnsafeGetFullKey()
        {
            return _redis.PrimaryRedisDb.KeySpace + _key;
        }

        /// <summary>
        /// Interface for callbacks to control mirroring of redis keys
        /// </summary>
        internal interface IReplicatedKeyHost
        {
            /// <nodoc />
            Tracer Tracer { get; }

            /// <summary>
            /// Gets whether the instance can mirror
            /// </summary>
            bool CanMirror { get; }

            /// <summary>
            /// Gets the interval for mirroring
            /// </summary>
            TimeSpan MirrorInterval { get; }
        }
    }
}
