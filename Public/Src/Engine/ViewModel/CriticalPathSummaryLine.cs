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
