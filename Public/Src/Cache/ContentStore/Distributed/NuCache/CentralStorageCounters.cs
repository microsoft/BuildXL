// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Counters used by <see cref="CentralStorage"/>.
    /// </summary>
    public enum CentralStorageCounters
    {
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        TryGetFile,

        /// <nodoc />
        TryGetFileFromPeerSucceeded,

        /// <nodoc />
        TryGetFileFromFallback,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        TouchBlob,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        UploadFile,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        UploadShardFile,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        CollectStaleBlobs,

        [CounterType(CounterType.Stopwatch)]
        PruneInternalCache,
    }
}
