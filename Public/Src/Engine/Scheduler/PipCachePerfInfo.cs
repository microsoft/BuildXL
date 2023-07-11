// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
        /// <nodoc/>
        public PipCacheMissType CacheMissType { get; private set; }

        /// <nodoc/>
        public int NumPathSetsDownloaded { get; private set; }

        /// <nodoc/>
        public int NumCacheEntriesVisited { get; private set; }

        /// <summary>
        /// Represent <see cref="PipExecutorCounter"/> counters during CacheLookup
        /// </summary>
        public (long durationTicks, long occurrences)[] BeforeExecutionCacheStepCounters;

        /// <summary>
        /// Represent <see cref="PipExecutorCounter"/> counters during storing to cache during ExecuteProcess step
        /// </summary>
        public (long durationTicks, long occurrences)[] AfterExecutionCacheStepCounters;

        /// <nodoc/>
        public PipCachePerfInfo()
        {
            BeforeExecutionCacheStepCounters = new(long, long)[OperationKind.TrackedCacheLookupCounterCount];
            AfterExecutionCacheStepCounters = new (long, long)[OperationKind.TrackedCacheLookupCounterCount];
        }

        /// <nodoc/>
        public PipCachePerfInfo((long durationTicks, long occurrences)[] beforeExecutionCacheStepCounters, (long durationTicks, long occurrences)[] afterExecutionCacheStepCounters, PipCacheMissType cacheMissType, int numPathSetsDownloaded, int numCacheEntriesVisited)
        {
            BeforeExecutionCacheStepCounters = beforeExecutionCacheStepCounters;
            AfterExecutionCacheStepCounters = afterExecutionCacheStepCounters;
            CacheMissType = cacheMissType;
            NumPathSetsDownloaded = numPathSetsDownloaded;
            NumCacheEntriesVisited = numCacheEntriesVisited;
        }

        /// <nodoc/>
        public void LogCacheLookupStep(PipExecutionStep step, OperationKind kind, TimeSpan value)
        {
            var cacheStepCounters = step == PipExecutionStep.CacheLookup ? BeforeExecutionCacheStepCounters : AfterExecutionCacheStepCounters;
            Interlocked.Increment(ref cacheStepCounters[kind.CacheLookupCounterId].occurrences);
            Interlocked.Add(ref cacheStepCounters[kind.CacheLookupCounterId].durationTicks, value.Ticks);
        }

        /// <nodoc/>
        public void LogCounters(PipCacheMissType cacheMissType, int numPathSetsDownloaded, int numCacheEntriesVisited)
        {
            CacheMissType = cacheMissType;
            NumPathSetsDownloaded += numPathSetsDownloaded;
            NumCacheEntriesVisited += numCacheEntriesVisited;
        }

        /// <nodoc/>
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(BeforeExecutionCacheStepCounters.Length);
            foreach (var tuple in BeforeExecutionCacheStepCounters)
            {
                writer.WriteCompact(tuple.durationTicks);
                writer.WriteCompact(tuple.occurrences);
            }

            writer.WriteCompact(AfterExecutionCacheStepCounters.Length);
            foreach (var tuple in AfterExecutionCacheStepCounters)
            {
                writer.WriteCompact(tuple.durationTicks);
                writer.WriteCompact(tuple.occurrences);
            }

            writer.Write((byte)CacheMissType);
            writer.WriteCompact(NumPathSetsDownloaded);
            writer.WriteCompact(NumCacheEntriesVisited);
        }

        /// <nodoc/>
        public static PipCachePerfInfo Deserialize(BuildXLReader reader)
        {
            int count = reader.ReadInt32Compact();
            var beforeExecutionCacheStepCounters = new(long durationTicks, long occurrences)[count];
            for (int i = 0; i < count; i++)
            {
                beforeExecutionCacheStepCounters[i].durationTicks = reader.ReadInt64Compact();
                beforeExecutionCacheStepCounters[i].occurrences = reader.ReadInt64Compact();
            }

            count = reader.ReadInt32Compact();
            var afterExecutionCacheStepCounters = new (long durationTicks, long occurrences)[count];
            for (int i = 0; i < count; i++)
            {
                afterExecutionCacheStepCounters[i].durationTicks = reader.ReadInt64Compact();
                afterExecutionCacheStepCounters[i].occurrences = reader.ReadInt64Compact();
            }

            PipCacheMissType cacheMissType = (PipCacheMissType)reader.ReadByte();
            int numPathSetsDownloaded = reader.ReadInt32Compact();
            int numCacheEntriesVisited = reader.ReadInt32Compact();
            return new PipCachePerfInfo(beforeExecutionCacheStepCounters, afterExecutionCacheStepCounters, cacheMissType, numPathSetsDownloaded, numCacheEntriesVisited);
        }
    }
}
