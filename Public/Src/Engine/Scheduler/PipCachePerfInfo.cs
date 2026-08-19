// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// CacheLookup counters per pip, which is also transferred from workers to orchestrator.
    /// </summary>
    public sealed class PipCachePerfInfo
    {
        // Each counter stores up to ~20.4 days of exact TimeSpan ticks and 1,048,575 occurrences.
        // On overflow, the values will get capped at the maximum values
        private const int OccurrenceBits = 20;
        internal const long MaxRepresentableDurationTicks = (1L << (64 - OccurrenceBits)) - 1;
        internal const long MaxRepresentableOccurrences = (1L << OccurrenceBits) - 1;
        private const uint MaxOccurrences = (uint)MaxRepresentableOccurrences;
        private const ulong OccurrenceMask = MaxOccurrences;
        private const ulong MaxDurationTicks = (ulong)MaxRepresentableDurationTicks;

        // Some tracked operations can occur both during CacheLookup and during other execution steps. Separate
        // phase-specific counter mappings could avoid unused slots, but would need to preserve operations that occur
        // on both sides and remain synchronized with the serialized counter ordering.
        private long[] m_beforeExecutionCacheStepCounters;
        private long[] m_afterExecutionCacheStepCounters;

        /// <summary>
        /// A cache operation and its accumulated performance values.
        /// </summary>
        internal readonly record struct CacheStepCounter(
            OperationKind OperationKind,
            long DurationTicks,
            long Occurrences);

        /// <nodoc/>
        public PipCacheMissType CacheMissType { get; private set; }

        /// <nodoc/>
        public int NumPathSetsDownloaded { get; private set; }

        /// <nodoc/>
        public int NumCacheEntriesVisited { get; private set; }

        /// <nodoc/>
        public int NumCacheEntriesAbsent { get; private set; }

        /// <nodoc/>
        public PipCachePerfInfo()
        {
        }

        private PipCachePerfInfo(long[] beforeExecutionCacheStepCounters, long[] afterExecutionCacheStepCounters, PipCacheMissType cacheMissType, int numPathSetsDownloaded, int numCacheEntriesVisited, int numCacheEntriesAbsent)
        {
            m_beforeExecutionCacheStepCounters = beforeExecutionCacheStepCounters;
            m_afterExecutionCacheStepCounters = afterExecutionCacheStepCounters;
            CacheMissType = cacheMissType;
            NumPathSetsDownloaded = numPathSetsDownloaded;
            NumCacheEntriesVisited = numCacheEntriesVisited;
            NumCacheEntriesAbsent = numCacheEntriesAbsent;
        }

        /// <nodoc/>
        public void LogCacheLookupStep(PipExecutionStep step, OperationKind kind, TimeSpan value)
        {
            int counterId = GetCounterId(kind);
            var cacheStepCounters = GetOrCreateCounters(step == PipExecutionStep.CacheLookup);
            ulong addedDurationTicks = DurationTicksToPackedValue(value.Ticks);

            while (true)
            {
                long currentValue = Volatile.Read(ref cacheStepCounters[counterId]);
                UnpackCounter(currentValue, out ulong durationTicks, out uint occurrences);
                long updatedValue = PackCounterInternal(
                    AddSaturating(durationTicks, addedDurationTicks),
                    occurrences == MaxOccurrences ? MaxOccurrences : occurrences + 1);

                if (Interlocked.CompareExchange(ref cacheStepCounters[counterId], updatedValue, currentValue) == currentValue)
                {
                    return;
                }
            }
        }

        /// <nodoc/>
        public void LogCounters(PipCacheMissType cacheMissType, int numPathSetsDownloaded, int numCacheEntriesVisited, int numCacheEntriesAbsent)
        {
            CacheMissType = cacheMissType;
            NumPathSetsDownloaded += numPathSetsDownloaded;
            NumCacheEntriesVisited += numCacheEntriesVisited;
            NumCacheEntriesAbsent += numCacheEntriesAbsent;
        }

        internal CacheStepCounter GetBeforeExecutionCounter(OperationKind operationKind)
        {
            return GetCounter(m_beforeExecutionCacheStepCounters, operationKind);
        }

        internal CacheStepCounter GetAfterExecutionCounter(OperationKind operationKind)
        {
            return GetCounter(m_afterExecutionCacheStepCounters, operationKind);
        }

        private long[] GetOrCreateCounters(bool beforeExecution)
        {
            ref long[] counters = ref beforeExecution
                ? ref m_beforeExecutionCacheStepCounters
                : ref m_afterExecutionCacheStepCounters;

            var result = Volatile.Read(ref counters);
            if (result != null)
            {
                return result;
            }

            var newCounters = new long[OperationKind.TrackedCacheLookupCounterCount];
            return Interlocked.CompareExchange(ref counters, newCounters, null) ?? newCounters;
        }

        private static CacheStepCounter GetCounter(long[] counters, OperationKind operationKind)
        {
            int counterId = GetCounterId(operationKind);
            if (counters == null)
            {
                return new CacheStepCounter(operationKind, 0, 0);
            }

            UnpackCounter(Volatile.Read(ref counters[counterId]), out ulong durationTicks, out uint occurrences);
            return new CacheStepCounter(operationKind, (long)durationTicks, occurrences);
        }

        private static int GetCounterId(OperationKind operationKind)
        {
            int counterId = operationKind.CacheLookupCounterId;
            if (counterId < 0 || counterId >= OperationKind.TrackedCacheLookupCounterCount)
            {
                throw new ArgumentException($"Operation '{operationKind}' is not a tracked cache operation.", nameof(operationKind));
            }

            return counterId;
        }

        /// <summary>
        /// Packs duration and occurrence values that are already within their representable bounds.
        /// </summary>
        /// <remarks>
        /// Callers must ensure the duration does not exceed MaxRepresentableDurationTicks and the occurrence count
        /// does not exceed MaxRepresentableOccurrences.
        /// </remarks>
        private static long PackCounterInternal(ulong boundedDurationTicks, uint boundedOccurrences)
        {
            return unchecked((long)((boundedDurationTicks << OccurrenceBits) | boundedOccurrences));
        }

        /// <summary>
        /// Clamps arbitrary signed duration and occurrence values to their representable bounds before packing them.
        /// </summary>
        private static long PackCounter(long durationTicks, long occurrences)
        {
            return PackCounterInternal(
                DurationTicksToPackedValue(durationTicks),
                occurrences >= MaxOccurrences ? MaxOccurrences : (uint)Math.Max(0, occurrences));
        }

        private static void UnpackCounter(long packedCounter, out ulong durationTicks, out uint occurrences)
        {
            ulong value = unchecked((ulong)packedCounter);
            durationTicks = value >> OccurrenceBits;
            occurrences = (uint)(value & OccurrenceMask);
        }

        private static ulong DurationTicksToPackedValue(long durationTicks)
        {
            if (durationTicks <= 0)
            {
                return 0;
            }

            ulong ticks = (ulong)durationTicks;
            return ticks >= MaxDurationTicks ? MaxDurationTicks : ticks;
        }

        private static ulong AddSaturating(ulong left, ulong right)
        {
            ulong sum = left + right;
            return sum < left || sum >= MaxDurationTicks ? MaxDurationTicks : sum;
        }

        #region Serialization

        /// <nodoc/>
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(OperationKind.TrackedCacheLookupCounterCount);
            for (int i = 0; i < OperationKind.TrackedCacheLookupCounterCount; i++)
            {
                CacheStepCounter counter = GetCounter(m_beforeExecutionCacheStepCounters, OperationKind.GetTrackedCacheOperationKind(i));
                writer.WriteCompact(counter.DurationTicks);
                writer.WriteCompact(counter.Occurrences);
            }

            writer.WriteCompact(OperationKind.TrackedCacheLookupCounterCount);
            for (int i = 0; i < OperationKind.TrackedCacheLookupCounterCount; i++)
            {
                CacheStepCounter counter = GetCounter(m_afterExecutionCacheStepCounters, OperationKind.GetTrackedCacheOperationKind(i));
                writer.WriteCompact(counter.DurationTicks);
                writer.WriteCompact(counter.Occurrences);
            }

            writer.Write((byte)CacheMissType);
            writer.WriteCompact(NumPathSetsDownloaded);
            writer.WriteCompact(NumCacheEntriesVisited);
            writer.WriteCompact(NumCacheEntriesAbsent);
        }

        /// <nodoc/>
        public static PipCachePerfInfo Deserialize(BuildXLReader reader)
        {
            long[] beforeExecutionCacheStepCounters = DeserializeCounters(reader);
            long[] afterExecutionCacheStepCounters = DeserializeCounters(reader);

            PipCacheMissType cacheMissType = (PipCacheMissType)reader.ReadByte();
            int numPathSetsDownloaded = reader.ReadInt32Compact();
            int numCacheEntriesVisited = reader.ReadInt32Compact();
            int numCacheEntriesAbsent = reader.ReadInt32Compact();
            return new PipCachePerfInfo(beforeExecutionCacheStepCounters, afterExecutionCacheStepCounters, cacheMissType, numPathSetsDownloaded, numCacheEntriesVisited, numCacheEntriesAbsent);
        }

        private static long[] DeserializeCounters(BuildXLReader reader)
        {
            int count = reader.ReadInt32Compact();
            if (count < 0)
            {
                throw new InvalidDataException($"Invalid cache lookup counter count: {count}");
            }

            long[] packedCounters = null;

            // Consume every counter sent by a newer worker, but retain only the counters this version understands.
            for (int i = 0; i < count; i++)
            {
                long durationTicks = reader.ReadInt64Compact();
                long occurrences = reader.ReadInt64Compact();
                if (i < OperationKind.TrackedCacheLookupCounterCount)
                {
                    long packedCounter = PackCounter(durationTicks, occurrences);
                    if (packedCounter != 0)
                    {
                        packedCounters ??= new long[OperationKind.TrackedCacheLookupCounterCount];
                        packedCounters[i] = packedCounter;
                    }
                }
            }

            return packedCounters;
        }

        #endregion

        #region Duration aggregation

        // These methods accumulate packed per-pip values into caller-owned totals without exposing the backing storage or allocating snapshots.

        internal void AddBeforeExecutionDurationsTo(long[] durations)
        {
            AddDurationsTo(m_beforeExecutionCacheStepCounters, durations);
        }

        internal void AddAfterExecutionDurationsTo(long[] durations)
        {
            AddDurationsTo(m_afterExecutionCacheStepCounters, durations);
        }

        private static void AddDurationsTo(long[] counters, long[] durations)
        {
            if (counters == null)
            {
                return;
            }

            for (int i = 0; i < counters.Length; i++)
            {
                UnpackCounter(Volatile.Read(ref counters[i]), out ulong durationTicks, out _);
                durations[i] += (long)durationTicks / TimeSpan.TicksPerMillisecond;
            }
        }

        #endregion
    }
}
