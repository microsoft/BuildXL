// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Performance counters available for <see cref="ContentLocationDatabase"/>.
    /// </summary>
    public enum ContentLocationDatabaseCounters
    {
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PutLocations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetLocations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GarbageCollect,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        LocationAdded,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        LocationRemoved,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        ContentTouched,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        SaveCheckpoint,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RestoreCheckpoint,

        /// <nodoc />
        TotalNumberOfCreatedEntries,

        /// <nodoc />
        TotalNumberOfSkippedEntryTouches,

        /// <nodoc />
        TotalNumberOfDeletedEntries,

        /// <summary>
        /// Total number of entries collected during the garbage collection process.
        /// </summary>
        TotalNumberOfCollectedEntries,

        /// <summary>
        /// Total number of entries cleaned during the garbage collection process (i.e. entries that were updated based on inactive machines).
        /// </summary>
        TotalNumberOfCleanedEntries,

        /// <summary>
        /// Total number of entries cleaned during the garbage collection process.
        /// </summary>
        TotalNumberOfScannedEntries,

        /// <nodoc />
        TotalNumberOfCacheHit,

        /// <nodoc />
        TotalNumberOfCacheMiss,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        CacheFlush,

        /// <nodoc />
        TotalNumberOfCacheFlushes,

        /// <nodoc />
        NumberOfCacheFlushesTriggeredByUpdates,

        /// <nodoc />
        NumberOfCacheFlushesTriggeredByTimer,

        /// <nodoc />
        NumberOfCacheFlushesTriggeredByGarbageCollection,

        /// <nodoc />
        NumberOfCacheFlushesTriggeredByCheckpoint,
    }
}
