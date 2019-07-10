// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Diagnostics.Tracing;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Tracing.CloudBuild
{
    /// <summary>
    /// Event reporting the end of BuildXL process
    /// </summary>
    // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
    [EventData]
    public sealed class DominoCompletedEvent : CloudBuildEvent
    {
        private static readonly PropertyInfo[] s_members = typeof(DominoCompletedEvent).GetProperties();

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
        public override EventKind Kind { get; set; } = EventKind.DominoCompleted;

        /// <summary>
        /// Event time in terms of Utc ticks
        /// </summary>
        public long UtcTicks { get; set; }

        /// <summary>
        /// Exit code from the BuildXL process
        /// </summary>
        /// <remarks>
        /// Non-zero values represent a failure.
        /// </remarks>
        public int ExitCode { get; set; }

        /// <summary>
        /// High level categorization of what was performed
        /// </summary>
        public ExitKind ExitKind { get; set; }
    }
}
