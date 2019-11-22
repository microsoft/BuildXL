// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Class in charge of putting and getting blobs to/from Redis.
    /// There is a limit to how many bytes it can put into Redis, which is enforced through a reservation strategy. 
    /// </summary>
    internal sealed class RedisBlobAdapter
    {
        public enum RedisBlobAdapterCounters
        {
            [CounterType(CounterType.Stopwatch)]
            PutBlob,

            [CounterType(CounterType.Stopwatch)]
            GetBlob,

            SkippedBlobs,
            FailedReservations,
            DownloadedBytes,
            DownloadedBlobs
        }

        private readonly CounterCollection<RedisBlobAdapterCounters> _counters = new CounterCollection<RedisBlobAdapterCounters>();

        private readonly RedisDatabaseAdapter _redis;
        private readonly TimeSpan _blobExpiryTime;
        private readonly TimeSpan _capacityExpiryTime;
        private string _lastFailedReservationKey;
        private readonly long _maxCapacityPerTimeBox;
        private readonly IClock _clock;
        private readonly Tracer _tracer;

        internal static string GetBlobKey(ContentHash hash) => $"Blob-{hash}";

        public RedisBlobAdapter(RedisDatabaseAdapter redis, TimeSpan blobExpiryTime, long maxCapacity, IClock clock, Tracer tracer)
        {
            _redis = redis;
            _blobExpiryTime = blobExpiryTime;
            _capacityExpiryTime = blobExpiryTime.Add(TimeSpan.FromMinutes(5));
            _maxCapacityPerTimeBox = maxCapacity / 2;
            _clock = clock;
            _tracer = tracer;
        }

        /// <nodoc />
        public class PutBlobResult : BoolResult
        {
            /// <nodoc />
            public ContentHash Hash { get; }

            /// <nodoc />
            public long BlobSize { get; }

            /// <nodoc />
            public bool AlreadyInRedis { get; }

            /// <nodoc />
            public long? NewCapacityInRedis { get; }

            /// <nodoc />
            public string RedisKey { get; }

            /// <nodoc />
            public PutBlobResult(ContentHash hash, long blobSize, bool alreadyInRedis = false, long? newCapacity = null, string redisKey = null)
            {
                Hash = hash;
                BlobSize = blobSize;
                AlreadyInRedis = alreadyInRedis;
                NewCapacityInRedis = newCapacity;
                RedisKey = redisKey;
            }

            /// <nodoc />
            public PutBlobResult(ContentHash hash, long blobSize, string errorMessage)
                : base(errorMessage)
            {
                Hash = hash;
                BlobSize = blobSize;
            }

            /// <nodoc />
            public PutBlobResult(ResultBase other, string message)
                : base(other, message)
            {
            }

            /// <inheritdoc />
            public override string ToString()
            {
                string baseResult = $"Hash=[{Hash.ToShortString()}], BlobSize=[{BlobSize}]";
                if (Succeeded)
                {
                    if (AlreadyInRedis)
                    {
                        return $"{baseResult}, AlreadyInRedis=[{AlreadyInRedis}]";
                    }

                    return $"{baseResult}. AlreadyInRedis=[False], RedisKey=[{RedisKey}], NewCapacity=[{NewCapacityInRedis}].";
                }

                return $"{baseResult}. {ErrorMessage}";
            }
        }

        /// <summary>
        ///     Puts a blob into Redis. Will fail only if capacity cannot be reserved or if Redis fails in some way.
        /// </summary>
        public Task<PutBlobResult> PutBlobAsync(OperationContext context, ContentHash hash, byte[] blob)
        {
            return context.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    var key = GetBlobKey(hash);

                    if (await _redis.KeyExistsAsync(context, key, context.Token))
                    {

                        _counters[RedisBlobAdapterCounters.SkippedBlobs].Increment();
                        return new PutBlobResult(hash, blob.Length, alreadyInRedis: true);
                    }

                    var reservationResult = await TryReserveAsync(context, blob.Length, hash);
                    if (!reservationResult)
                    {
                        _counters[RedisBlobAdapterCounters.FailedReservations].Increment();
                        return new PutBlobResult(hash, blob.Length, reservationResult.ErrorMessage);
                    }

                    var success = await _redis.StringSetAsync(context, key, blob, _blobExpiryTime, StackExchange.Redis.When.Always, context.Token);
                    return success
                        ? new PutBlobResult(hash, blob.Length, alreadyInRedis: false, newCapacity: reservationResult.Value.newCapacity, redisKey: reservationResult.Value.key)
                        : new PutBlobResult(hash, blob.Length, "Redis value could not be updated to upload blob.");
                },
                traceOperationStarted: false,
                counter: _counters[RedisBlobAdapterCounters.PutBlob]);
        }

        /// <summary>
        ///     The reservation strategy consists of timeboxes of 30 minutes, where each box only has half the max permitted
        /// capacity. This is to account for Redis not deleting files exactly when their TTL expires.
        ///     Under this scheme, each blob will try to add its length to its box's capacity and fail if max capacity has
        /// been exceeded.
        /// </summary>
        private async Task<Result<(long newCapacity, string key)>> TryReserveAsync(OperationContext context, long byteCount, ContentHash hash)
        {
            var operationStart = _clock.UtcNow;
            var time = new DateTime(ticks: operationStart.Ticks / _blobExpiryTime.Ticks * _blobExpiryTime.Ticks);
            var key = $"BlobCapacity@{time.ToString("yyyyMMdd:hhmmss.fff")}";

            if (key == _lastFailedReservationKey)
            {
                string message = $"Skipping reservation for blob [{hash.ToShortString()}] because key [{key}] ran out of capacity.";
                return Result.FromErrorMessage<(long newCapacity, string key)>(message);
            }
            
            var newUsedCapacity = await _redis.ExecuteBatchAsync(context, async batch =>
            {
                var stringSetTask = batch.StringSetAsync(key, 0, _capacityExpiryTime, StackExchange.Redis.When.NotExists);
                var incrementTask = batch.StringIncrementAsync(key, byValue: byteCount);

                await Task.WhenAll(stringSetTask, incrementTask);
                return await incrementTask;
            }, RedisOperation.StringIncrement);

            var couldReserve = newUsedCapacity <= _maxCapacityPerTimeBox;

            if (!couldReserve)
            {
                _lastFailedReservationKey = key;
                string error = $"Could not reserve {byteCount} for {hash.ToShortString()} because key [{key}] ran out of capacity. Expected new capacity={newUsedCapacity} bytes, Max capacity={_maxCapacityPerTimeBox} bytes.";
                return Result.FromErrorMessage<(long newCapacity, string key)>(error);
            }

            return Result.Success((newUsedCapacity, key));
        }

        /// <summary>
        ///     Tries to get a blob from Redis.
        /// </summary>
        public Task<Result<byte[]>> GetBlobAsync(OperationContext context, ContentHash hash)
        {
            return context.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    byte[] result = await _redis.StringGetAsync(context, GetBlobKey(hash), context.Token);

                    if (result == null)
                    {
                        return new Result<byte[]>($"Blob for hash=[{hash.ToShortString()}] was not found.");
                    }

                    _counters[RedisBlobAdapterCounters.DownloadedBytes].Add(result.Length);
                    _counters[RedisBlobAdapterCounters.DownloadedBlobs].Increment();
                    return new Result<byte[]>(result);
                },
                traceOperationStarted: false,
                extraEndMessage: result => result.Succeeded ? $"Hash=[{hash.ToShortString()}], Size=[{result.Value.Length}]" : $"Hash=[{hash.ToShortString()}]",
                counter: _counters[RedisBlobAdapterCounters.GetBlob]);
        }

        public CounterSet GetCounters() => _counters.ToCounterSet();
    }
}
