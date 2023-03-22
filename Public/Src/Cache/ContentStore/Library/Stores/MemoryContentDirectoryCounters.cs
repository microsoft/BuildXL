// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

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
