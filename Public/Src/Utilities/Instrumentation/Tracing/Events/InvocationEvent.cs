// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Diagnostics.Tracing;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Tracing.CloudBuild
{
    /// <summary>
    /// Event reporting the beginning of BuildXL process
    /// </summary>
    // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
    [EventData]
    public sealed class DominoInvocationEvent : CloudBuildEvent
    {
        private static readonly PropertyInfo[] s_members = typeof(DominoInvocationEvent).GetProperties();

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
        public override EventKind Kind { get; set; } = EventKind.DominoInvocation;

        /// <summary>
        /// Event time in terms of Utc ticks
        /// </summary>
        public long UtcTicks { get; set; }

        /// <summary>
        /// Execution environment
        /// </summary>
        public ExecutionEnvironment Environment { get; set; }

        /// <summary>
        /// Commandline arguments
        /// </summary>
        public string CommandLineArgs { get; set; }

        /// <summary>
        /// BuildXL version
        /// </summary>
        // $Rename: Due to telemetry backend scripts this cannot be renamed to BuildXL
        public string DominoVersion { get; set; }

        /// <summary>
        /// Log directory
        /// </summary>
        public string LogDirectory { get; set; }
    }
}
