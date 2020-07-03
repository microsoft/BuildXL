// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        SetMachineStateDeadUnavailable,

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

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        SetMachineStateOpen,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        SetMachineStateClosed,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        SetMachineStateUnknown,
    }
}
