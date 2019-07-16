// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Diagnostics.Tracing;

namespace BuildXL.Tracing.CloudBuild
{
    /// <summary>
    /// Event reporting a snapshot of BuildXL's execution statistics.  Sent periodically while the build is running.
    /// </summary>
    // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
    [EventData]
    public sealed class DominoContinuousStatisticsEvent : CloudBuildEvent
    {
        private static readonly PropertyInfo[] s_members = typeof(DominoContinuousStatisticsEvent).GetProperties();

        /// <inheritdoc />
        internal override PropertyInfo[] Members => s_members;

        /// <summary>
        /// Event version
        /// </summary>
        /// <remarks>
        /// WARNING: INCREMENT IF YOU UPDATE THE PRIMITIVE MEMBERS!
        /// </remarks>
        public override int Version { get; set; } = 2;

        /// <inheritdoc />
        public override EventKind Kind { get; set; } = EventKind.DominoContinuousStatistics;

        /// <summary>
        /// Total number of pips.
        /// </summary>
        public long TotalPips { get; set; }

        /// <summary>
        /// Total number of process pips.
        /// </summary>
        public long TotalProcessPips { get; set; }

        /// <summary>
        /// Number of failed pips.
        /// </summary>
        public long PipsFailed { get; set; }

        /// <summary>
        /// Number of skipped pips due to failed dependencies.
        /// </summary>
        public long PipsSkippedDueToFailedDependencies { get; set; }

        /// <summary>
        /// Number of pips executed successfully.
        /// </summary>
        public long PipsSuccessfullyExecuted { get; set; }

        /// <summary>
        /// Number of currently executing pips.
        /// </summary>
        public long PipsExecuting { get; set; }

        /// <summary>
        /// Number of pips that are ready to run and waiting on resources.
        /// </summary>
        public long PipsReadyToRun { get; set; }

        /// <summary>
        /// Number of process pips executed (success or fail).
        /// </summary>
        public long ProcessPipsExecuted { get; set; }

        /// <summary>
        /// Number of process pips that are successfully executed from cache.
        /// </summary>
        public long ProcessPipsExecutedFromCache { get; set; }
    }
}
