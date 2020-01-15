// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Performance counters available for <see cref="ResourcePool{TKey, TObject}"/>.
    /// </summary>
    public enum ResourcePoolCounters
    {
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        Cleanup,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        CreationTime,

        /// <nodoc />
        Created,

        /// <nodoc />
        Cleaned,

        /// <nodoc />
        Reused,
    }
}
