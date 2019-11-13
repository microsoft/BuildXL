// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// CacheLookup counters per pip, which is also transferred from workers to master.
    /// </summary>
    public sealed class CacheLookupPerfInfo
    {
        /// <nodoc/>
        public PipCacheMissType CacheMissType { get; private set; }

        /// <nodoc/>
        public int NumPathSetsDownloaded { get; private set; }

        /// <nodoc/>
        public int NumCacheEntriesVisited { get; private set; }

        /// <nodoc/>
        public (long durationTicks, long occurrences)[] CacheLookupStepCounters;

        /// <nodoc/>
        public CacheLookupPerfInfo()
        {
            CacheLookupStepCounters = new(long, long)[OperationKind.TrackedCacheLookupCounterCount];
        }

        /// <nodoc/>
        public CacheLookupPerfInfo((long durationTicks, long occurrences)[] cacheLookupCounters, PipCacheMissType cacheMissType, int numPathSetsDownloaded, int numCacheEntriesVisited)
        {
            CacheLookupStepCounters = cacheLookupCounters;
            CacheMissType = cacheMissType;
            NumPathSetsDownloaded = numPathSetsDownloaded;
            NumCacheEntriesVisited = numCacheEntriesVisited;
        }

        /// <nodoc/>
        public void LogCacheLookupStep(OperationKind kind, TimeSpan value)
        {
            Interlocked.Increment(ref CacheLookupStepCounters[kind.CacheLookupCounterId].occurrences);
            Interlocked.Add(ref CacheLookupStepCounters[kind.CacheLookupCounterId].durationTicks, value.Ticks);
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
            writer.WriteCompact(CacheLookupStepCounters.Length);

            foreach (var tuple in CacheLookupStepCounters)
            {
                writer.WriteCompact(tuple.durationTicks);
                writer.WriteCompact(tuple.occurrences);
            }

            writer.Write((byte)CacheMissType);
            writer.WriteCompact(NumPathSetsDownloaded);
            writer.WriteCompact(NumCacheEntriesVisited);
        }

        /// <nodoc/>
        public static CacheLookupPerfInfo Deserialize(BuildXLReader reader)
        {
            int count = reader.ReadInt32Compact();

            var cacheLookupStepCounters = new(long durationTicks, long occurrences)[count];
            for (int i = 0; i < count; i++)
            {
                cacheLookupStepCounters[i].durationTicks = reader.ReadInt64Compact();
                cacheLookupStepCounters[i].occurrences = reader.ReadInt64Compact();
            }

            PipCacheMissType cacheMissType = (PipCacheMissType)reader.ReadByte();
            int numPathSetsDownloaded = reader.ReadInt32Compact();
            int numCacheEntriesVisited = reader.ReadInt32Compact();
            return new CacheLookupPerfInfo(cacheLookupStepCounters, cacheMissType, numPathSetsDownloaded, numCacheEntriesVisited);
        }
    }
}
