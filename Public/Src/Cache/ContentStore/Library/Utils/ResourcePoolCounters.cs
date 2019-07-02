// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
