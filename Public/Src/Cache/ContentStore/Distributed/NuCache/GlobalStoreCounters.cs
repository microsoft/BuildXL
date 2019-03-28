// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Counters used by <see cref="RedisGlobalStore"/>.
    /// </summary>
    public enum GlobalStoreCounters
    {
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RegisterLocalLocation,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RegisterLocalLocationUpdate,

        /// <nodoc />
        RegisterLocalLocationHashCount,

        /// <nodoc />
        RegisterLocalLocationUpdateHashCount,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetCheckpointState,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        TryGetCheckpoint,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        ReleaseRole,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        UpdateRole,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RegisterCheckpoint,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        UpdateClusterState,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        InvalidateLocalMachine,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetBulk,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        InfoStats,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        PutBlob,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetBlob,

        /// <nodoc />
        GetBulkEntrySingleResult,
    }
}
