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
    public sealed class TargetFinishedEvent : CloudBuildEvent
    {
        private static readonly PropertyInfo[] s_members = typeof(TargetFinishedEvent).GetProperties();

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
        public override EventKind Kind { get; set; } = EventKind.TargetFinished;

        /// <summary>
        /// Event time in terms of Utc ticks
        /// </summary>
        public long UtcTicks { get; set; }

        /// <summary>
        /// Target id
        /// </summary>
        public int TargetId { get; set; }

        /// <summary>
        /// Build result (success or fail)
        /// </summary>
        public BuildResult Result { get; set; }
    }

    /// <summary>
    /// Result of the build
    /// </summary>
    public enum BuildResult
    {
        /// <summary>
        /// Succeeded
        /// </summary>
        Succeeded,

        /// <summary>
        /// Failed
        /// </summary>
        Failed,

        /// <summary>
        /// RetrievedFromCache
        /// </summary>
        RetrievedFromCache,
    }
}
