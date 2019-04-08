// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Model class for build progress
    /// </summary>
    public sealed class ProgressModel
    {
        /// <summary>
        /// Total number of scheduled pips
        /// </summary>
        public long TotalPipsCount { get; set; }

        /// <summary>
        /// Number of waiting pips
        /// </summary>
        public long WaitingPipsCount { get; set; }

        /// <summary>
        /// Number of queued and ready pips
        /// </summary>
        public long ReadyPipsCount { get; set; }

        /// <summary>
        /// Number of pips currently running
        /// </summary>
        public long RunningPipsCount { get; set; }

        /// <summary>
        /// Number of successfully completed pips
        /// </summary>
        public long DonePipsCount { get; set; }

        /// <summary>
        /// Number of failed pips
        /// </summary>
        public long FailedPipsCount { get; set; }

        /// <summary>
        /// Number of skipped pips
        /// </summary>
        public long SkippedPipsCount { get; set; }

        /// <summary>
        /// Number of ignored pips
        /// </summary>
        public long IgnoredPipsCount { get; set; }
    }
}
