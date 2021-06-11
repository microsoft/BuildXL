// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;
#if MICROSOFT_INTERNAL
using Microsoft.Caching.Redis;
using Microsoft.Caching.Redis.KeyspaceIsolation;
#else
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
#endif

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Class in charge of putting and getting blobs to/from Redis.
    /// There is a limit to how many bytes it can put into Redis, which is enforced through a reservation strategy. 
    /// </summary>
    internal sealed class RedisBlobAdapter
    {
        public enum Counters
        {
            SkippedBlobs,
            FailedReservations,
            DownloadedBytes,
            DownloadedBlobs,
            OperationsThrottled,
            PutBlobSuccess,
        }

        private readonly RedisDatabaseAdapter _redis;
        private readonly TimeSpan _blobExpiryTime;
        private readonly TimeSpan _capacityExpiryTime;
        private string? _lastFailedCapacityKey;
        private readonly long _maxCapacityPerTimeBox;
        private readonly IClock _clock;

        private readonly OperationThrottle _operationThrottle;

        private readonly CounterCollection<Counters> _counters = new CounterCollection<Counters>();

        internal static string GetBlobKey(ShortHash hash) => $"Blob-{hash}";

        /// <nodoc />
        public RedisBlobAdapter(RedisDatabaseAdapter redis, IClock clock, RedisContentLocationStoreConfiguration configuration)
        {
            _redis = redis;
            _blobExpiryTime = configuration.BlobExpiryTime;
            _capacityExpiryTime = _blobExpiryTime.Add(TimeSpan.FromMinutes(5));
            _maxCapacityPerTimeBox = configuration.MaxBlobCapacity / 2;
            _clock = clock;

            _operationThrottle = new OperationThrottle(configuration.BlobOperationLimitSpan, configuration.BlobOperationLimitCount, clock);
        }

        /// <summary>
        ///     Puts a blob into Redis. Will fail only if capacity cannot be reserved or if Redis fails in some way.
        /// </summary>
        public async Task<PutBlobResult> PutBlobAsync(OperationContext context, ShortHash hash, byte[] blob)
        {
            const string ErrorMessage = "Redis value could not be updated to upload blob.";
            try
            {
                var key = GetBlobKey(hash);

                var capacityKey = GetCurrentCapacityKey(operationStart: _clock.UtcNow);

                // It's important that we check capacity before contacting Redis, since we want to avoid unnecessary trips.
                if (!IsCapacityValid(capacityKey))
                {
                    _counters[Counters.FailedReservations].Increment();
                    return PutBlobResult.OutOfCapacity(hash, blob.Length, capacityKey);
                }

                BoolResult throttleCheck = _operationThrottle.CheckAndRegisterOperation();
                if (!throttleCheck)
                {
                    _counters[Counters.OperationsThrottled].Increment();
                    Contract.AssertNotNull(throttleCheck.ErrorMessage);
                    return PutBlobResult.Throttled(hash, blob.Length, capacityKey, throttleCheck.ErrorMessage);
                }

                if (await _redis.KeyExistsAsync(context, key, context.Token))
                {
                    _counters[Counters.SkippedBlobs].Increment();
                    return PutBlobResult.RedisHasAlready(hash, blob.Length, key);
                }

                var reservationResult = await TryReserveAsync(context, blob.Length, capacityKey);
                if (!reservationResult)
                {
                    _lastFailedCapacityKey = capacityKey;
                    _counters[Counters.FailedReservations].Increment();
                    Contract.AssertNotNull(reservationResult.ErrorMessage);
                    return PutBlobResult.OutOfCapacity(hash, blob.Length, capacityKey, reservationResult.ErrorMessage);
                }

                var success = await _redis.StringSetAsync(context, key, blob, _blobExpiryTime, When.Always, context.Token);

                if (success)
                {
                    _counters[Counters.PutBlobSuccess].Increment();
                    return PutBlobResult.NewRedisEntry(hash, blob.Length, reservationResult.Value.key, reservationResult.Value.newCapacity);
                }

                return new PutBlobResult(hash, blob.Length, ErrorMessage);
            }
            catch (Exception e)
            {
                return new PutBlobResult(hash, blob.Length, new ErrorResult(e), ErrorMessage);
            }
        }

        /// <summary>
        ///     The reservation strategy consists of timeboxes of 30 minutes, where each box only has half the max permitted
        /// capacity. This is to account for Redis not deleting files exactly when their TTL expires.
        ///     Under this scheme, each blob will try to add its length to its box's capacity and fail if max capacity has
        /// been exceeded.
        /// </summary>
        private async Task<Result<(long newCapacity, string key)>> TryReserveAsync(OperationContext context, long byteCount, string capacityKey)
        {
            var newUsedCapacity = await _redis.ExecuteBatchAsync(context, async batch =>
            {
                var stringSetTask = batch.StringSetAsync(capacityKey, 0, _capacityExpiryTime, When.NotExists);
                var incrementTask = batch.StringIncrementAsync(capacityKey, byValue: byteCount);

                await Task.WhenAll(stringSetTask, incrementTask);
                return await incrementTask;
            }, RedisOperation.StringIncrement);

            var couldReserve = newUsedCapacity <= _maxCapacityPerTimeBox;

            if (!couldReserve)
            {
                var message = $"Could not reserve {byteCount} bytes because key ran out of capacity. Expected new capacity={newUsedCapacity} bytes, Max capacity={_maxCapacityPerTimeBox} bytes.";
                return Result.FromErrorMessage<(long newCapacity, string key)>(message);
            }

            return Result.Success((newUsedCapacity, capacityKey));
        }

        private string GetCurrentCapacityKey(DateTime operationStart)
        {
            // Floor to the nearest multiple of _blobExpiryTime.Ticks.
            var time = new DateTime(ticks: operationStart.Ticks - (operationStart.Ticks % _blobExpiryTime.Ticks));
            return $"BlobCapacity@{time:yyyyMMdd:hhmmss.fff}";
        }

        private bool IsCapacityValid(string capacityKey) => capacityKey != _lastFailedCapacityKey;

        /// <summary>
        ///     Tries to get a blob from Redis.
        /// </summary>
        public async Task<GetBlobResult> GetBlobAsync(OperationContext context, ShortHash hash)
        {
            try
            {
                BoolResult limitCheck = _operationThrottle.CheckAndRegisterOperation();
                if (!limitCheck)
                {
                    _counters[Counters.OperationsThrottled].Increment();
                    return new GetBlobResult(limitCheck, hash);
                }

                byte[] result = await _redis.StringGetAsync(context, GetBlobKey(hash), context.Token);

                if (result == null)
                {
                    return new GetBlobResult(hash, blob: null);
                }

                _counters[Counters.DownloadedBytes].Add(result.Length);
                _counters[Counters.DownloadedBlobs].Increment();
                return new GetBlobResult(hash, result);
            }
            catch (Exception e)
            {
                return new GetBlobResult(new ErrorResult(e), "Blob could not be fetched from redis.", hash);
            }
        }

        public CounterSet GetCounters() => _counters.ToCounterSet();
    }
}
