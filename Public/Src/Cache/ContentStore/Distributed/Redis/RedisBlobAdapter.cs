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

        /// <summary>
        ///     Puts a blob into Redis. Will fail only if capacity cannot be reserved or if Redis fails in some way.
        /// </summary>
        public Task<BoolResult> PutBlobAsync(OperationContext context, ContentHash hash, byte[] blob)
        {
            return context.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    var key = GetBlobKey(hash);

                    if (await _redis.KeyExists(context, key, context.Token))
                    {
                        context.TraceDebug($"Hash=[{hash.ToShortString()}] is already in Redis. PutBlob skipped.");
                        _counters[RedisBlobAdapterCounters.SkippedBlobs].Increment();
                        return BoolResult.Success;
                    }

                    if (!await TryReserveAsync(context, blob.Length, hash))
                    {
                        _counters[RedisBlobAdapterCounters.FailedReservations].Increment();
                        return new BoolResult("Failed to reserve space for blob.");
                    }

                    var success = await _redis.StringSetAsync(context, key, blob, _blobExpiryTime, StackExchange.Redis.When.Always, context.Token);
                    return success ? BoolResult.Success : new BoolResult("Redis value could not be updated to upload blob.");
                },
                extraStartMessage: $"Size=[{blob.Length}]",
                counter: _counters[RedisBlobAdapterCounters.PutBlob]);
        }

        /// <summary>
        ///     The reservation strategy consists of timeboxes of 30 minutes, where each box only has half the max permitted
        /// capacity. This is to account for Redis not deleting files exactly when their TTL expires.
        ///     Under this scheme, each blob will try to add its length to its box's capacity and fail if max capacity has
        /// been exceeded.
        /// </summary>
        private async Task<bool> TryReserveAsync(OperationContext context, long byteCount, ContentHash hash)
        {
            var operationStart = _clock.UtcNow;
            var time = new DateTime(ticks: operationStart.Ticks / _blobExpiryTime.Ticks * _blobExpiryTime.Ticks);
            var key = $"BlobCapacity@{time.ToString("yyyyMMdd:hhmmss.fff")}";

            if (key == _lastFailedReservationKey)
            {
                context.TraceDebug($"Skipping reservation for blob [{hash.ToShortString()}] because key [{key}] has already been used in a previous failed reservation");
                return false;
            }
            
            var newUsedCapacity = await _redis.ExecuteBatchAsync(context, async batch =>
            {
                var stringSetTask = batch.StringSetAsync(key, 0, _capacityExpiryTime, StackExchange.Redis.When.NotExists);
                var incrementTask = batch.StringIncrementAsync(key, byValue: byteCount);

                await Task.WhenAll(stringSetTask, incrementTask);
                return await incrementTask;
            }, RedisOperation.StringIncrement);

            var couldReserve = newUsedCapacity <= _maxCapacityPerTimeBox;
            context.TraceDebug($"{(couldReserve ? "Successfully reserved" : "Could not reserve")} {byteCount} bytes in {key} for {hash.ToShortString()}. New used capacity: {newUsedCapacity} bytes");

            if (!couldReserve)
            {
                _lastFailedReservationKey = key;
            }

            return couldReserve;
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
                extraEndMessage: result => result.Succeeded ? $"Size=[{result.Value.Length}]" : null,
                counter: _counters[RedisBlobAdapterCounters.GetBlob]);
        }

        public CounterSet GetCounters() => _counters.ToCounterSet();
    }
}
