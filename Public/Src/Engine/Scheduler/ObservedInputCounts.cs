// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Count of each ObservedInput type encountered
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815")]
    public struct ObservedInputCounts
    {
        /// <nodoc/>
        public int AbsentPathProbeCount;

        /// <nodoc/>
        public int FileContentReadCount;

        /// <nodoc/>
        public int DirectoryEnumerationCount;

        /// <nodoc/>
        public int ExistingDirectoryProbeCount;

        /// <nodoc/>
        public int ExistingFileProbeCount;

        /// <summary>
        /// Returns an object that is the max of the returned items
        /// </summary>
        public ObservedInputCounts Max(ObservedInputCounts other)
        {
            return other.Sum > Sum ? other : this;
        }

        private int Sum => AbsentPathProbeCount + FileContentReadCount + DirectoryEnumerationCount + ExistingDirectoryProbeCount + ExistingFileProbeCount;

        /// <summary>
        /// Computes the max expected size of prior runs. If the prior runs have a larger count, something probably isn't correct
        /// </summary>
        public ObservedInputCounts ComputeMaxExpected()
        {
            return new ObservedInputCounts
                   {
                        AbsentPathProbeCount = AbsentPathProbeCount * 2,
                        FileContentReadCount = FileContentReadCount * 2,
                        DirectoryEnumerationCount = DirectoryEnumerationCount * 2,
                        ExistingDirectoryProbeCount = ExistingDirectoryProbeCount * 2,
                        ExistingFileProbeCount = ExistingFileProbeCount * 2
                    };
        }

        /// <summary>
        /// Logs a warning of the new observed input count is dramatically lower than existing counts
        /// </summary>
        public static void LogForLowObservedInputs(
            LoggingContext loggingContext,
            string pipDescription,
            ObservedInputCounts executionCounts,
            ObservedInputCounts cacheMaxCounts)
        {
            var maxExpected = executionCounts.ComputeMaxExpected();
            if (cacheMaxCounts.AbsentPathProbeCount > maxExpected.AbsentPathProbeCount ||
                cacheMaxCounts.DirectoryEnumerationCount > maxExpected.DirectoryEnumerationCount ||
                cacheMaxCounts.ExistingDirectoryProbeCount > maxExpected.ExistingDirectoryProbeCount ||
                cacheMaxCounts.FileContentReadCount > maxExpected.FileContentReadCount ||
                cacheMaxCounts.ExistingFileProbeCount > maxExpected.ExistingFileProbeCount)
            {
                Logger.Log.UnexpectedlySmallObservedInputCount(
                    loggingContext,
                    pipDescription,
                    cacheMaxCounts.AbsentPathProbeCount,
                    cacheMaxCounts.DirectoryEnumerationCount,
                    cacheMaxCounts.ExistingDirectoryProbeCount,
                    cacheMaxCounts.FileContentReadCount,
                    executionCounts.AbsentPathProbeCount,
                    executionCounts.DirectoryEnumerationCount,
                    executionCounts.ExistingDirectoryProbeCount,
                    executionCounts.FileContentReadCount,
                    executionCounts.ExistingFileProbeCount);
            }
        }
    }
}
