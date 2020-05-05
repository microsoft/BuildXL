// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Counters used by <see cref="IContentLocationStore"/> implementations.
    /// </summary>
    public enum ContentLocationStoreCounters
    {
        /// <nodoc />
        DatabaseStoreAdd,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        DatabaseGet,

        /// <nodoc />
        EvictionMinAge,

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
        RegisterLocalLocation,

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
        StaleLastAccessTimeUpdates,

        /// <nodoc />
        IncrementalCheckpointFilesDownloaded,

        /// <nodoc />
        IncrementalCheckpointFilesDownloadSkipped,

        /// <nodoc />
        IncrementalCheckpointFilesUploaded,

        /// <nodoc />
        IncrementalCheckpointFilesUploadSkipped,

        /// <nodoc />
        ReconciliationCycles,

        /// <nodoc />
        RestoreCheckpointsSkipped,

        /// <nodoc />
        RestoreCheckpointsSucceeded,
    }
}
