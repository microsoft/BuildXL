// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Vfs
{
    /// <summary>
    /// Counters associated with the VFS
    /// </summary>
    internal enum VfsCounters
    {
        PlaceHydratedFileUnknownSizeCount,
        PlaceHydratedFileBytes,

        [CounterType(CounterType.Stopwatch)]
        PlaceHydratedFile,

        [CounterType(CounterType.Stopwatch)]
        TryCreateSymlink,
    }
}
