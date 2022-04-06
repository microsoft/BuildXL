// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.Tracing;
using System.Reflection;

namespace BuildXL.Tracing.CloudBuild
{
    /// <summary>
    /// Event reporting progress of targets
    /// </summary>
    [EventData]
    public sealed class TargetFailedEvent : CloudBuildEvent
    {
        private static readonly PropertyInfo[] s_members = typeof(TargetFailedEvent).GetProperties();

        /// <inheritdoc />
        internal override PropertyInfo[] Members => s_members;

        /// <summary>
        /// Event version
        /// </summary>
        /// <remarks>
        /// WARNING: INCREMENT IF YOU UPDATE THE PRIMITIVE MEMBERS!
        /// </remarks>
        public override int Version { get; set; } = 3;

        /// <inheritdoc />
        public override EventKind Kind { get; set; } = EventKind.TargetFailed;

        /// <summary>
        /// Worker id
        /// </summary>
        public string? WorkerId { get; set; }

        /// <summary>
        /// Target id
        /// </summary>
        public string? TargetId { get; set; }

        /// <summary>
        /// File path that contains the output of standard error stream
        /// </summary>
        public string? StdErrorPath { get; set; }

        /// <summary>
        /// Pip description
        /// </summary>
        public string? PipDescription { get; set; }

        /// <summary>
        /// Target Start time 
        /// </summary>
        public DateTime TargetStartTime { get; set; }

        /// <summary>
        /// Standard Output path
        /// </summary>
        public string? StdOutputPath { get; set; }

        /// <summary>
        /// Short Pip Description for Target Error Path
        /// </summary>
        public string? ShortPipDescription { get; set; }

        /// <summary>
        /// PipExecutionTime
        /// </summary>
        public long PipExecutionTimeMs { get; set; }

    }
}
