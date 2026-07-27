// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.ViewModel
{
    /// <summary>
    /// A line item in the critical path summary
    /// </summary>
    public class CriticalPathSummaryLine
    {
        /// <nodoc />
        public TimeSpan PipDuration { get; set; }

        /// <nodoc />
        public TimeSpan ProcessExecuteTime { get; set; }

        /// <summary>
        /// When <see cref="ProcessExecuteTime"/> was injected via a <c>##bxl[runtimeSecs]</c> hint, holds the originally measured
        /// execution time; <c>null</c> when the value was measured (i.e. no injection occurred).
        /// </summary>
        public TimeSpan? OriginalProcessExecuteTime { get; set; }

        /// <nodoc />
        public TimeSpan PipQueueDuration { get; set; }

        /// <nodoc />
        public string Result { get; set; }

        /// <nodoc />
        public TimeSpan ScheduleTime { get; set; }

        /// <nodoc />
        public TimeSpan Completed { get; set; }

        /// <nodoc />
        public string PipDescription { get; set; }
    }
}
