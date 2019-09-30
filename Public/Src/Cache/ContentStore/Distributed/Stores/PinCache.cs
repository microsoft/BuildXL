// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// Caches remote pin operations in-memory for a specified time to live to prevent unnecessary remote calls
    /// </summary>
    public class PinCache
    {
        private readonly IClock _clock;
        private readonly Counter _pinCacheHitCounter;
        private readonly Counter _pinCacheMissCounter;
        private readonly Counter _pinCacheExpiredCounter;
        private readonly Counter _pinCacheCleanedCounter;

        /// <nodoc />
        protected readonly List<Counter> Counters = new List<Counter>();

        private readonly ConcurrentDictionary<ContentHash, PinInfo> _cachedPins = new ConcurrentDictionary<ContentHash, PinInfo>();
        private readonly ConcurrentQueue<ContentHash> _garbageCollectionQueue = new ConcurrentQueue<ContentHash>();

        /// <nodoc />
        public PinCache(IClock clock = null)
        {
            _clock = clock ?? SystemClock.Instance;
            Counters.Add(_pinCacheHitCounter = new Counter("PinCacheHitCount"));
            Counters.Add(_pinCacheMissCounter = new Counter("PinCacheMissCount"));
            Counters.Add(_pinCacheExpiredCounter = new Counter("PinCacheExpiredCount"));
            Counters.Add(_pinCacheCleanedCounter = new Counter("PinCacheCleanedCount"));
        }

        /// <summary>
        /// Creates a pinner which attempts to satisfy pins from local cached results before falling back to remote pin
        /// </summary>
        public RemotePinAsync CreatePinner(RemotePinAsync remotePinAsync)
        {
            Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
                Context context,
                IReadOnlyList<ContentHash> contentHashes,
                CancellationToken cts,
                bool pinWithPlace,
                UrgencyHint urgencyHint = UrgencyHint.Nominal)
            {
                return Workflows.RunWithFallback<ContentHash, PinResult>(
                        contentHashes,
                        hashes => PinFromCachedResultsAsync(context, hashes, cts, urgencyHint),
                        hashes => remotePinAsync(context, hashes, cts, pinWithPlace, urgencyHint),
                        result => result.Succeeded);
            }

            return PinAsync;
        }

        /// <summary>
        /// Get pin cache counters
        /// </summary>
        public CounterSet GetCounters(Context context)
        {
            var counterSet = new CounterSet();
            foreach (var counter in Counters)
            {
                counterSet.Add(counter.Name, counter.Value);
            }

            return counterSet;
        }

        /// <summary>
        /// Sets the cached pin information
        /// </summary>
        public void SetPinInfo(ContentHash hash, TimeSpan timeToLive)
        {
            if (timeToLive <= TimeSpan.Zero)
            {
                Invalidate(hash);
                return;
            }

            var expiryTime = _clock.UtcNow + timeToLive;
            var pinInfo = new PinInfo(expiryTime);
            if (_cachedPins.TryAdd(hash, pinInfo))
            {
                // Only add to the garbage collection queue if the hash is newly added
                _garbageCollectionQueue.Enqueue(hash);
            }
            else
            {
                _cachedPins[hash] = pinInfo;
            }

            // Attempt to clean up a stale hash. This supplements the remove if expired logic which
            // would never clean up hashes if they are not pinned again.
            CleanStaleHashes(dequeueCount: 1);
        }


        /// <summary>
        /// Dequeues hashes from the garbage collection queue and removes them if they have expired. Otherwise, hash is added back to garbage collection queue.
        /// This supplements the remove if expired logic which would never clean up hashes if they are not pinned again.
        /// </summary>
        private void CleanStaleHashes(int dequeueCount)
        {
            for (int i = 0; i < dequeueCount; i++)
            {
                if (_garbageCollectionQueue.TryDequeue(out var hash))
                {
                    var cachedPinResult = GetCachedPinResult(hash);
                    if (cachedPinResult == CachedPinResult.Hit)
                    {
                        // Pin result is still valid. Add it back to queue
                        _garbageCollectionQueue.Enqueue(hash);
                    }
                    else if (cachedPinResult == CachedPinResult.Expired)
                    {
                        // Remove the hash
                        Invalidate(hash);
                        _pinCacheCleanedCounter.Increment();
                    }
                }
                else
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Removes the hash from the pin cache
        /// </summary>
        public void Invalidate(ContentHash hash)
        {
            PinInfo removedInfo;
            _cachedPins.TryRemove(hash, out removedInfo);
        }

        private Task<IEnumerable<Task<Indexed<PinResult>>>> PinFromCachedResultsAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            // Capture timestamp to avoid unneccesary
            return Task.FromResult(contentHashes.Select(hash => PinFromCachedResult(hash)).AsIndexed().Select(r => Task.FromResult(r)));
        }

        /// <summary>
        /// Attempts to pin the given hash using cached results
        /// </summary>
        public PinResult TryPinFromCachedResult(ContentHash hash)
        {
            return PinFromCachedResult(hash);
        }

        /// <summary>
        /// Gets the pin result assuming the content is pinned.
        /// </summary>
        private PinResult PinFromCachedResult(ContentHash hash)
        {
            var cachedPinResult = GetCachedPinResult(hash);
            switch (cachedPinResult)
            {
                case CachedPinResult.Miss:
                    _pinCacheMissCounter.Increment();
                    return PinResult.ContentNotFound;
                case CachedPinResult.Expired:
                    _pinCacheExpiredCounter.Increment();
                    return PinResult.ContentNotFound;
                case CachedPinResult.Hit:
                default:
                    Contract.Assert(cachedPinResult == CachedPinResult.Hit);
                    _pinCacheHitCounter.Increment();
                    return PinResult.Success;
            }
        }

        /// <summary>
        /// Gets the cached pin result
        /// </summary>
        private CachedPinResult GetCachedPinResult(ContentHash hash)
        {
            PinInfo pinInfo;
            if (_cachedPins.TryGetValue(hash, out pinInfo))
            {
                if (pinInfo.ExpirationTimeUtc > _clock.UtcNow)
                {
                    return CachedPinResult.Hit;
                }
                else
                {
                    return CachedPinResult.Expired;
                }
            }

            return CachedPinResult.Miss;
        }

        /// <summary>
        /// Describes the states of content in the pin cache
        /// </summary>
        private enum CachedPinResult
        {
            /// <summary>
            /// Pin for content is not present in pin cache
            /// </summary>
            Miss,

            /// <summary>
            /// Pin for content is present in pin cache but has expired
            /// </summary>
            Expired,

            /// <summary>
            /// Pin for content is present in pin cache and is still applicable
            /// </summary>
            Hit,
        }

        /// <summary>
        /// Defines cached pin information. Currently, only has the TTL, but may have other information in the future.
        /// </summary>
        private readonly struct PinInfo
        {
            /// <summary>
            /// The TTL for the cached pin
            /// </summary>
            public readonly DateTime ExpirationTimeUtc;

            public PinInfo(DateTime expirationTimeUtc)
            {
                ExpirationTimeUtc = expirationTimeUtc;
            }
        }
    }
}
