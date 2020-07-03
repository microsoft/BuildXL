// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.Tracing;
using System.Reflection;

namespace BuildXL.Tracing.CloudBuild
{
    /// <summary>
    /// Event reporting final statistics regarding BuildXL's execution.
    /// Sent at the end of the execution phase (i.e., after all pips have finished),
    /// but before <see cref="DominoCompletedEvent"/>.  This event is not necessarily
    /// sent if BuildXL crashes.
    /// </summary>
    // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
    [EventData]
    public sealed class DominoFinalStatisticsEvent : CloudBuildEvent
    {
        private static readonly PropertyInfo[] s_members = typeof(DominoFinalStatisticsEvent).GetProperties();

        /// <inheritdoc />
        internal override PropertyInfo[] Members => s_members;

        /// <summary>
        /// Event version
        /// </summary>
        /// <remarks>
        /// WARNING: INCREMENT IF YOU UPDATE THE PRIMITIVE MEMBERS!
        /// </remarks>
        public override int Version { get; set; } = 1;

        /// <inheritdoc />
        public override EventKind Kind { get; set; } = EventKind.DominoFinalStatistics;

        /// <summary>
        /// Time when last non-drop pip completed (in terms of Utc ticks); 0 indicates that no non-drop pips were executed.
        /// </summary>
        public long LastNonDropPipCompletionUtcTicks { get; set; }

        /// <summary>
        /// Time when last drop pip completed (in terms of Utc ticks); 0 indicates that no drop pips were executed.
        /// </summary>
        public long LastDropPipCompletionUtcTicks { get; set; }

        /// <summary>
        /// Drop overhang time: difference between <see cref="LastDropPipCompletionUtcTicks"/> and <see cref="LastNonDropPipCompletionUtcTicks"/>.
        /// </summary>
        public TimeSpan CalculateDropOverhang()
        {
            return new DateTime(LastDropPipCompletionUtcTicks).Subtract(new DateTime(LastNonDropPipCompletionUtcTicks));
        }
    }
}
