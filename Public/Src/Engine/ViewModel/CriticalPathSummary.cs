// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace BuildXL.ViewModel
{
    /// <summary>
    /// Critical path summary view model
    /// </summary>
    public class CriticalPathSummary
    {
        /// <nodoc />
        public List<CriticalPathSummaryLine> Lines { get; } = new List<CriticalPathSummaryLine>();

        /// <nodoc />
        public TimeSpan TotalCriticalPathRuntime { get; set; }

        /// <nodoc />
        public TimeSpan ExeDurationCriticalPath { get; set; }

        /// <nodoc />
        public TimeSpan TotalMasterQueueTime { get; set; }

        /// <nodoc />
        internal void RenderMarkdown(MarkDownWriter writer)
        {
            writer.StartDetailedTableSummary(
                "Critical path",
                $"Pip Duration: {TotalCriticalPathRuntime.MakeFriendly()}, Exe Duration: {ExeDurationCriticalPath.MakeFriendly()}");
            writer.StartTable(
                "Pip Duration",
                "Exe Duration",
                "Queue Duration",
                "Pip Result",
                "Scheduled Time",
                "Completed Time",
                "Pip");
            writer.WriteTableRow(
                TotalCriticalPathRuntime.MakeFriendly(),
                ExeDurationCriticalPath.MakeFriendly(),
                TotalMasterQueueTime.MakeFriendly(),
                string.Empty,
                string.Empty,
                string.Empty,
                "*Total");
            writer.WriteTableRow(
                "-",
                "-",
                "-",
                "-",
                "-",
                "-",
                "-");
            foreach (var line in Lines)
            {
                writer.WriteTableRow(
                    line.PipDuration.MakeFriendly(),
                    line.ProcessExecuteTime.MakeFriendly(),
                    line.PipQueueDuration.MakeFriendly(),
                    line.Result,
                    line.ScheduleTime.MakeFriendly(),
                    line.Completed.MakeFriendly(),
                    line.PipDescription
                );
            }

            writer.EndTable();
            writer.EndDetailedTableSummary();
        }

    }
}
