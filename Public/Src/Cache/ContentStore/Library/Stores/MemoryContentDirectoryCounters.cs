// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Performance counters for <see cref="MemoryContentDirectory"/>.
    /// </summary>
    public enum MemoryContentDirectoryCounters
    {
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        InitializeContentDirectory,
    }
}
