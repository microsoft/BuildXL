// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Sharded, low-contention counters for hot concurrent caches.
    /// </summary>
    /// <remarks>
    /// Each thread updates one padded shard using a non-atomic increment. Updates can be lost when
    /// threads map to the same shard, which is an intentional tradeoff to avoid a globally contended
    /// atomic operation. Reads sum the shards using volatile loads, but do not provide a consistent
    /// snapshot while updates are in progress.
    /// </remarks>
    /// <remarks>
    /// Individual shards may wrap to keep the update path inexpensive. A read returns
    /// <see cref="long.MaxValue"/> when it observes a wrapped shard or when the aggregate would
    /// overflow, making the abnormal state visible without allowing diagnostic counters to throw.
    /// </remarks>
    internal sealed class ApproximateCacheCounters
    {
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        private struct CounterShard
        {
            [FieldOffset(0)]
            public long Hits;

            [FieldOffset(8)]
            public long Misses;
        }

        private readonly CounterShard[] m_shards;

        public ApproximateCacheCounters()
        {
            int shardCount = 1;
            int targetShardCount = Math.Min(Environment.ProcessorCount, 64);
            while (shardCount < targetShardCount)
            {
                shardCount <<= 1;
            }

            m_shards = new CounterShard[shardCount];
        }

        public void RecordHit()
        {
            ref long value = ref m_shards[Environment.CurrentManagedThreadId & (m_shards.Length - 1)].Hits;
            unchecked
            {
                value++;
            }
        }

        public void RecordMiss()
        {
            ref long value = ref m_shards[Environment.CurrentManagedThreadId & (m_shards.Length - 1)].Misses;
            unchecked
            {
                value++;
            }
        }

        public long Hits => Sum(isHit: true);

        public long Misses => Sum(isHit: false);

        private long Sum(bool isHit)
        {
            long result = 0;
            for (int i = 0; i < m_shards.Length; i++)
            {
                long value = isHit
                    ? Volatile.Read(ref m_shards[i].Hits)
                    : Volatile.Read(ref m_shards[i].Misses);
                result = AddForAggregation(result, value);
            }

            return result;
        }

        internal static long AddForAggregation(long current, long value)
        {
            // A negative shard has wrapped. Saturate before performing arithmetic that could overflow.
            return value < 0 || long.MaxValue - current < value
                ? long.MaxValue
                : current + value;
        }
    }
}
