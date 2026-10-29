// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Engine.DirectGraph
{
    /// <summary>
    /// Statistics for DirectGraph operations handled by BuildXL.
    /// </summary>
    public enum DirectGraphCounter
    {
        AddProcessCount,

        [CounterType(CounterType.Stopwatch)]
        AddProcessDuration,

        AddWriteFileCount,

        [CounterType(CounterType.Stopwatch)]
        AddWriteFileDuration,

        AddCopyFileCount,

        [CounterType(CounterType.Stopwatch)]
        AddCopyFileDuration,

        AddSealDirectoryCount,

        [CounterType(CounterType.Stopwatch)]
        AddSealDirectoryDuration,

        AddCompositeSharedOpaqueDirectoryCount,

        [CounterType(CounterType.Stopwatch)]
        AddCompositeSharedOpaqueDirectoryDuration,

        AddExportCount,

        [CounterType(CounterType.Stopwatch)]
        AddExportDuration,

        CompleteCount,

        [CounterType(CounterType.Stopwatch)]
        CompleteDuration,
    }
}
