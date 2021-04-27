// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Counters for <see cref="FileUtilities" />.
    /// </summary>
    public enum StorageCounters
    {
        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        ReadFileUsnByHandleDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        WriteUsnCloseRecordByHandleDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        ReadUsnJournalDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        GetFileAttributesByHandleDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        GetFileFlagsAndAttributesForPossibleReparsePointDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        GetReparsePointTypeDuration,

        /// <nodoc/>
        [CounterType(CounterType.Numeric)]
        CopyOnWriteCount,

        /// <nodoc/>
        [CounterType(CounterType.Numeric)]
        SuccessfulCopyOnWriteCount,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        CopyOnWriteDuration,

        /// <nodoc/>
        [CounterType(CounterType.Numeric)]
        InKernelFileCopyCount,

        /// <nodoc/>
        [CounterType(CounterType.Numeric)]
        SuccessfulInKernelFileCopyCount,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        InKernelFileCopyDuration,
    }
}
