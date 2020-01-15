// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Tracing;
using System.Reflection;

namespace BuildXL.Tracing.CloudBuild
{
    /// <summary>
    /// Event created for each target added to the graph
    /// </summary>
    [EventData]
    public sealed class TargetAddedEvent : CloudBuildEvent
    {
        private static readonly PropertyInfo[] s_members = typeof(TargetAddedEvent).GetProperties();

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
        public override EventKind Kind { get; set; } = EventKind.TargetAdded;

        /// <summary>
        /// Target id
        /// </summary>
        public int TargetId { get; set; }

        /// <summary>
        /// Name for the target (groupby::targetname)
        /// </summary>
        public string TargetName { get; set; }
    }
}
