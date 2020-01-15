// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Helper counters for <see cref="Tracing.OperationKind"/>s which are not logged as statistics
    /// </summary>
    public enum OperationCounter
    {
        /// <nodoc/>
        FileContentManagerRestoreContentInCache,

        /// <nodoc/>
        FileContentManagerHandleContentAvailability,

        /// <nodoc/>
        FileContentManagerDiscoverExistingContent,

        /// <nodoc/>
        FileContentManagerHandleContentAvailabilityLogContentAvailability,

        /// <nodoc/>
        ObservedInputProcessorComputePipFileSystemPaths,

        /// <nodoc/>
        ObservedInputProcessorReportUnexpectedAccess,
    }
}
