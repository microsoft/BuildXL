// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
