// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    }
}
