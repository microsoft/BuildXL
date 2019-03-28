// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Counters for <see cref="LocalDiskContentStore" />.
    /// </summary>
    public enum LocalDiskContentStoreCounter
    {
        /// <summary>
        /// The amount of time it took to flush file content from page-cache to the underlying filesystem.
        /// This is a potentially expensive barrier operation needed to get up-to-date USNs.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FlushFileToFilesystemTime,

        /// <summary>
        /// The amount of bytes of file content hashed
        /// </summary>
        HashFileContentSizeBytes,

        /// <summary>
        /// The amount of time spent in TryProbeForExistence
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        TryProbeAndTrackPathForExistenceTime,

        /// <summary>
        /// The amount of time spent in TrackAbsentPath
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        TrackAbsentPathTime,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryMaterializeTime,

        /// <summary>
        /// The amount of time spent in TryDiscoverAsync
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        TryDiscoverTime,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryDiscoverTime_TryGetKnownContentHash,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryDiscoverTime_GetReparsePointType,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryDiscoverTime_OpenProbeHandle,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryDiscoverTime_OpenReadHandle,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryDiscoverTime_HashByTargetPath,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryDiscoverTime_HashFileContent,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryDiscoverTime_HashFileContentWithSemaphore,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryDiscoverTime_TrackChangesToFile,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryTrackChangesToFileTime,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryGetFileNameTime,

        /// <summary>
        /// The number of times the local disk content store is queried for untracked paths
        /// </summary>
        UntrackedPathCalls,

        /// <summary>
        /// The number of times the local disk content store is queried for tracked paths
        /// </summary>
        TrackedPathCalls
    }
}
