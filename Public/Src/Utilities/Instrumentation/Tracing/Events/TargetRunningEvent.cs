// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Diagnostics.Tracing;

namespace BuildXL.Tracing.CloudBuild
{
    /// <summary>
    /// Event reporting progress of targets
    /// </summary>
    [EventData]
    public sealed class TargetRunningEvent : CloudBuildEvent
    {
        private static readonly PropertyInfo[] s_members = typeof(TargetRunningEvent).GetProperties();

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
        public override EventKind Kind { get; set; } = EventKind.TargetRunning;

        /// <summary>
        /// Event time in terms of Utc ticks
        /// </summary>
        public long UtcTicks { get; set; }

        /// <summary>
        /// Target id
        /// </summary>
        public int TargetId { get; set; }

        /// <summary>
        /// Represents the platform for which the target was built (first pip's qualifier)
        /// </summary>
        public string QualifierName { get; set; }
    }
}
