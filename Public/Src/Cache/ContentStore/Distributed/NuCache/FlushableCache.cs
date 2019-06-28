// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Threading;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// A cache over the key value store in <see cref="ContentLocationDatabase"/>; made especifically for the needs of
    /// that class.
    /// </summary>
    internal class FlushableCache
    {
        private readonly ContentLocationDatabaseConfiguration _configuration;
        private readonly ContentLocationDatabase _database;

        private ConcurrentBigMap<ShortHash, ContentLocationEntry> _cache = new ConcurrentBigMap<ShortHash, ContentLocationEntry>();
        private ConcurrentBigMap<ShortHash, ContentLocationEntry> _flushingCache = new ConcurrentBigMap<ShortHash, ContentLocationEntry>();

        private readonly SemaphoreSlim _flushMutex = new SemaphoreSlim(1);
        private readonly ReadWriteLock _exchangeLock = ReadWriteLock.Create();

        public FlushableCache(ContentLocationDatabaseConfiguration configuration, ContentLocationDatabase database)
        {
            _configuration = configuration;
            _database = database;
        }

        /// <nodoc />
        public void UnsafeClear()
        {
            // Order is important here, inverse order could cause deadlock.
            using (_flushMutex.AcquireSemaphore())
            using (_exchangeLock.AcquireWriteLock())
            {
                if (_cache.Count != 0)
                {
                    // Nothing guarantees that some number of updates couldn't have happened in between the last flush
                    // and this reset, because acquisition of the write lock happens after the flush lock. The only way
                    // to deal with this situation is to force a flush before this assignment and after locks have been
                    // acquired. However, this isn't required by our code right now; although it is a simple change.
                    _cache = new ConcurrentBigMap<ShortHash, ContentLocationEntry>();
                }
            }
        }

        /// <nodoc />
        public void Store(OperationContext context, ShortHash hash, ContentLocationEntry entry)
        {
            using (_exchangeLock.AcquireReadLock())
            {
                _cache[hash] = entry;
            }
        }

        /// <nodoc />
        public bool TryGetEntry(ShortHash hash, out ContentLocationEntry entry)
        {
            using (_exchangeLock.AcquireReadLock())
            {
                // The entry could be a tombstone, so we need to make sure the user knows content has actually been
                // deleted, which is why we check for null.
                if (_cache.TryGetValue(hash, out entry))
                {
                    _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Increment();
                    return entry != null;
                }
                else if (_flushingCache.TryGetValue(hash, out entry))
                {
                    _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].Increment();
                    return entry != null;
                }
            }

            _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].Increment();

            return false;
        }

        /// <nodoc />
        public async Task FlushAsync(OperationContext context)
        {
            // This lock is required to ensure no flushes happen concurrently. We may loose updates if that happens.
            // AcquireAsync is used so as to avoid multiple concurrent tasks just waiting; this way we return the
            // task to the thread pool in between.
            using (await _flushMutex.AcquireAsync())
            {
                PerformFlush(context);
            }
        }

        /// <summary>
        /// Needs to take the flushing lock. Called only from <see cref="FlushAsync(OperationContext)"/>. Refactored
        /// out for clarity.
        /// </summary>
        private void PerformFlush(OperationContext context)
        {
            _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCacheFlushes].Increment();

            using (_database.Counters[ContentLocationDatabaseCounters.CacheFlush].Start())
            {
                using (_exchangeLock.AcquireWriteLock())
                {
                    _flushingCache = _cache;
                    _cache = new ConcurrentBigMap<ShortHash, ContentLocationEntry>();
                }

                if (_configuration.FlushSingleTransaction)
                {
                    _database.PersistBatch(context, _flushingCache);
                }
                else
                {
                    var actionBlock = new ActionBlockSlim<KeyValuePair<ShortHash, ContentLocationEntry>>(_configuration.FlushDegreeOfParallelism, kv =>
                    {
                        // Do not lock on GetLock here, as it will cause a deadlock with
                        // SetMachineExistenceAndUpdateDatabase. It is correct not do take any locks as well, because
                        // no Store can happen while flush is running.
                        _database.Persist(context, kv.Key, kv.Value);
                    });

                    foreach (var kv in _flushingCache)
                    {
                        actionBlock.Post(kv);
                    }

                    actionBlock.Complete();
                    actionBlock.CompletionAsync().Wait();
                }

                _database.Counters[ContentLocationDatabaseCounters.NumberOfPersistedEntries].Add(_flushingCache.Count);

                if (_configuration.FlushPreservePercentInMemory > 0)
                {
                    int targetFlushingSize = (int)(_flushingCache.Count * _configuration.FlushPreservePercentInMemory);
                    int removeAmount = _flushingCache.Count - targetFlushingSize;

                    foreach (var key in _flushingCache.Keys.Take(removeAmount))
                    {
                        _flushingCache.RemoveKey(key);
                    }
                }
                else
                {
                    using (_exchangeLock.AcquireWriteLock())
                    {
                        _flushingCache = new ConcurrentBigMap<ShortHash, ContentLocationEntry>();
                    }
                }

                _database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCompletedCacheFlushes].Increment();
            }
        }
    }
}
