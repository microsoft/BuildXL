// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

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
