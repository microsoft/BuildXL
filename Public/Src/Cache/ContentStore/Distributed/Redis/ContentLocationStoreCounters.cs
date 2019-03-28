// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Counters used by <see cref="RedisContentLocationStoreBase"/>.
    /// </summary>
    public enum ContentLocationStoreCounters
    {
        /// <nodoc />
        DatabaseStoreUpdate,

        /// <nodoc />
        DatabaseStoreAdd,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        DatabaseGet,

        /// <nodoc />
        GetBulkLocalHashes,

        /// <nodoc />
        GetBulkGlobalHashes,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetBulkLocal,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetBulkGlobal,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        CompileLocations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        ProcessLocationMappings,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RegisterLocalLocation,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        ResolveLocations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        ResolveLocations_GetLocationMapping,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        ResolveLocations_CreateContentHashWithSizeAndLocations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        TrimBulkLocal,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetEffectiveLastAccessTimes,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        Reconcile,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        Reconcile_AddedContent,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        Reconcile_RemovedContent,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        BackgroundTouchBulk,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        InfoStats,

        // Subset of counters related to redis's garbage collection

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RedisGc_GarbageCollectShard,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RedisGc_GarbageCollectBatch,

        /// <summary>
        /// Number of keys cleaned because they are too old and contain no machines
        /// </summary>
        RedisGc_CleanedKeysCount,

        /// <summary>
        /// Number of keys cleanable because they are too old and contain no machines
        /// </summary>
        RedisGc_CleanableKeysCount,

        /// <summary>
        /// Number of keys cleaned because they are too old and contain no machines
        /// </summary>
        RedisGc_MalformedKeysCount,

        /// <summary>
        /// Number of entries retrieved during GC process.
        /// </summary>
        RedisGc_EntriesCount,

        /// <nodoc />
        LocationAddRecentInactiveEager,

        /// <nodoc />
        LocationAddRecentRemoveEager,

        /// <nodoc />
        LocationAddEager,

        /// <nodoc />
        RedundantLocationAddSkipped,

        /// <nodoc />
        RedundantRecentLocationAddSkipped,

        /// <nodoc />
        LocationAddQueued,

        /// <nodoc />
        LazyTouchEventOnly,

        /// <nodoc />
        EffectiveLastAccessTimeLookupHit,

        /// <nodoc />
        EffectiveLastAccessTimeLookupMiss,

        /// <nodoc />
        IncrementalCheckpointFilesDownloaded,

        /// <nodoc />
        IncrementalCheckpointFilesDownloadSkipped,

        /// <nodoc />
        IncrementalCheckpointFilesUploaded,

        /// <nodoc />
        IncrementalCheckpointFilesUploadSkipped,
    }
}
