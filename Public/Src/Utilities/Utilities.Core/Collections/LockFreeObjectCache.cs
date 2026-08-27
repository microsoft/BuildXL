// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities.Core;

#nullable disable

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Provides a bounded, lossy cache which maps a key to a value.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ObjectCache{TKey, TValue}"/>, which protects reads with striped reader-writer locks,
    /// ordinary cache operations do not acquire locks or wait for another operation. Reads return a miss if
    /// they observe a slot being written. Writes make one compare-and-swap attempt per candidate slot and drop
    /// the cache insertion if another writer owns or wins the slot; the value is still returned by
    /// <see cref="GetOrAdd{TState}(TKey, TState, Func{TKey, TState, TValue})"/>. Consequently, lookups and
    /// best-effort insertions are lock-free even when a competing thread is delayed while updating a slot.
    ///
    /// "Lock-free" describes only the cache's internal synchronization. A custom equality comparer or the
    /// factory passed to <see cref="GetOrAdd{TState}(TKey, TState, Func{TKey, TState, TValue})"/> executes user
    /// code and may block. Internally, each slot has a sequence number: an even version identifies a stable
    /// entry, while an odd version identifies a slot being updated. Readers compare the versions observed
    /// before and after copying an entry to reject concurrent or torn updates.
    ///
    /// The cache is a fixed-size array with two candidate slots per key. The primary slot is selected from the
    /// key's normalized comparer hash, and the backup slot is selected by mixing that hash with a fixed salt.
    /// This second distribution allows keys that collide at their primary indexes to retain independent backup
    /// locations. Reads probe both slots, and writes best-effort store a redundant copy in each slot, so a key can
    /// remain cached after one copy is displaced. This improves retention under collisions at the cost of using
    /// up to two slots per cached key; neither slot is guaranteed to retain the value.
    ///
    /// This cache is most suitable for read-heavy caches that experience lock contention and whose values are
    /// inexpensive to recreate if a write is dropped or an entry is evicted. The version and memory-ordering
    /// operations add per-entry storage and per-read overhead, so consumers with low contention, expensive value
    /// creation, or a working set sensitive to retention should benchmark both implementations before migrating.
    /// Hit and miss statistics are approximate under concurrency.
    /// </remarks>
    public sealed class LockFreeObjectCache<TKey, TValue>
    {
        private struct Entry
        {
            // Even versions are stable. Odd versions indicate that a writer owns the slot.
            public int Version;

            // This is the normalized primary hash or the rehashed backup hash. Zero represents an empty slot.
            public int ModifiedHashCode;
            public TKey Key;
            public TValue Value;
        }

        private readonly Entry[] m_slots;
        private readonly IEqualityComparer<TKey> m_comparer;
        private readonly ApproximateCacheCounters m_counters = new ApproximateCacheCounters();

        /// <summary>
        /// Gets the approximate number of cache hits.
        /// </summary>
        public long Hits => m_counters.Hits;

        /// <summary>
        /// Gets the approximate number of cache misses.
        /// </summary>
        public long Misses => m_counters.Misses;

        /// <summary>
        /// Gets the number of slots in the cache.
        /// </summary>
        public int Capacity => m_slots.Length;

        /// <summary>
        /// Constructs a new lossy cache.
        /// </summary>
        /// <param name="capacity">The number of slots available in the cache. For best results, this should be prime.</param>
        /// <param name="comparer">The equality comparer for computing hash codes and equality of keys.</param>
        public LockFreeObjectCache(int capacity, IEqualityComparer<TKey> comparer = null)
        {
            Contract.Requires(capacity > 0);

            m_slots = new Entry[capacity];
            m_comparer = comparer ?? EqualityComparer<TKey>.Default;
        }

        /// <summary>
        /// Attempts to retrieve the value for the specified key.
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            GetIndexes(key, out uint primaryIndex, out int primaryHashCode, out uint backupIndex, out int backupHashCode);
            if (TryGetValue(key, primaryIndex, primaryHashCode, out value)
                || TryGetValue(key, backupIndex, backupHashCode, out value))
            {
                m_counters.RecordHit();
                return true;
            }

            m_counters.RecordMiss();
            value = default;
            return false;
        }

        /// <summary>
        /// Adds an item to the cache.
        /// </summary>
        /// <returns>
        /// True if the item was absent from both candidate slots. Since insertion is best effort, true does not
        /// guarantee that the item was cached when another writer contends for either slot.
        /// </returns>
        public bool AddItem(TKey key, TValue value)
        {
            GetIndexes(key, out uint primaryIndex, out int primaryHashCode, out uint backupIndex, out int backupHashCode);

            bool primaryMiss = !ContainsKey(key, primaryIndex, primaryHashCode);
            if (primaryMiss)
            {
                TrySetEntry(primaryIndex, primaryHashCode, key, value);
            }

            bool backupMiss = !ContainsKey(key, backupIndex, backupHashCode);
            if (backupMiss)
            {
                TrySetEntry(backupIndex, backupHashCode, key, value);
            }

            return primaryMiss && backupMiss;
        }

        /// <summary>
        /// Gets an existing value or creates and attempts to cache a new value.
        /// </summary>
        /// <remarks>
        /// The factory may run concurrently more than once for the same key. Its result is returned even when
        /// contention prevents the result from being cached.
        /// </remarks>
        public TValue GetOrAdd<TState>(TKey key, TState state, Func<TKey, TState, TValue> factory)
        {
            Contract.Requires(factory != null);

            GetIndexes(key, out uint primaryIndex, out int primaryHashCode, out uint backupIndex, out int backupHashCode);
            if (TryGetValue(key, primaryIndex, primaryHashCode, out TValue value)
                || TryGetValue(key, backupIndex, backupHashCode, out value))
            {
                m_counters.RecordHit();
                return value;
            }

            m_counters.RecordMiss();
            value = factory(key, state);
            TrySetEntry(primaryIndex, primaryHashCode, key, value);
            TrySetEntry(backupIndex, backupHashCode, key, value);
            return value;
        }

        private bool ContainsKey(TKey key, uint index, int modifiedHashCode)
        {
            return TryReadEntry(index, out int candidateHashCode, out TKey candidateKey, out _)
                && candidateHashCode == modifiedHashCode
                && m_comparer.Equals(candidateKey, key);
        }

        private bool TryGetValue(TKey key, uint index, int modifiedHashCode, out TValue value)
        {
            if (TryReadEntry(index, out int candidateHashCode, out TKey candidateKey, out TValue candidateValue)
                && candidateHashCode == modifiedHashCode
                && m_comparer.Equals(candidateKey, key))
            {
                value = candidateValue;
                return true;
            }

            value = default;
            return false;
        }

        private bool TryReadEntry(uint index, out int modifiedHashCode, out TKey key, out TValue value)
        {
            ref Entry entry = ref m_slots[index];
            int initialVersion = Volatile.Read(ref entry.Version);
            if ((initialVersion & 1) != 0)
            {
                modifiedHashCode = 0;
                key = default;
                value = default;
                return false;
            }

            modifiedHashCode = entry.ModifiedHashCode;
            key = entry.Key;
            value = entry.Value;

            // Complete the optimistic snapshot before validating that no writer changed the slot.
            Thread.MemoryBarrier();
            int finalVersion = Volatile.Read(ref entry.Version);
            return initialVersion == finalVersion && modifiedHashCode != 0;
        }

        private void TrySetEntry(uint index, int modifiedHashCode, TKey key, TValue value)
        {
            ref Entry entry = ref m_slots[index];
            int stableVersion = Volatile.Read(ref entry.Version);
            if ((stableVersion & 1) != 0)
            {
                return;
            }

            // Claim the slot by changing its stable even version to an odd write version. Do not retry:
            // dropping a contested insertion keeps writes bounded and avoids waiting for the current owner.
            int writeVersion = unchecked(stableVersion + 1);
            if (Interlocked.CompareExchange(ref entry.Version, writeVersion, stableVersion) != stableVersion)
            {
                return;
            }

            entry.ModifiedHashCode = modifiedHashCode;
            entry.Key = key;
            entry.Value = value;

            // Publish the completed entry with the next even version. Unchecked wrapping preserves parity.
            Volatile.Write(ref entry.Version, unchecked(stableVersion + 2));
        }

        private void GetIndexes(
            TKey key,
            out uint primaryIndex,
            out int primaryHashCode,
            out uint backupIndex,
            out int backupHashCode)
        {
            // Hash mixing and conversions intentionally use modulo-2^32 arithmetic so every comparer-provided
            // hash code remains valid and overflow cannot make cache lookup throw in a checked build.
            unchecked
            {
                primaryHashCode = NormalizeHashCode(m_comparer.GetHashCode(key));
                primaryIndex = (uint)primaryHashCode % (uint)m_slots.Length;

                backupHashCode = NormalizeHashCode(HashCodeHelper.Combine(primaryHashCode, 17));
                backupIndex = (uint)backupHashCode % (uint)m_slots.Length;
            }
        }

        private static int NormalizeHashCode(int hashCode)
        {
            // Zero marks an empty slot in TryReadEntry, so it cannot represent a cached key's hash.
            return hashCode == 0 ? int.MaxValue : hashCode;
        }
    }
}
