// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration.Resolvers;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for the DirectGraph resolver.
    /// </summary>
    public interface IDirectGraphResolverSettings : IResolverSettings
    {
        /// <summary>
        /// The working directory for the tool.
        /// </summary>
        AbsolutePath WorkingDirectory { get; }

        /// <summary>
        /// The names of the modules owned by this resolver.
        /// </summary>
        /// <remarks>
        /// A name is a <see cref="PathAtom"/> because it also names the virtual spec file that represents the module.
        /// Names must be unique across all DirectGraph resolvers, and names that differ only in case are duplicates.
        /// The tool must refer to a module by exactly the name given here.
        /// </remarks>
        IReadOnlyList<PathAtom> ModuleNames { get; }

        /// <summary>
        /// The environment that is exposed to the tool. If not specified, the process environment is used.
        /// </summary>
        IReadOnlyDictionary<string, EnvironmentData> Environment { get; }

        /// <summary>
        /// Tool that contributes pips directly to the BuildXL graph.
        /// </summary>
        FileArtifact Tool { get; }

        /// <summary>
        /// Arguments passed to the tool.
        /// </summary>
        IReadOnlyList<string> Arguments { get; }

        /// <summary>
        /// How long, in minutes, the tool may go without sending an operation before it is terminated and the build fails.
        /// </summary>
        /// <remarks>
        /// This is the gap between two consecutive operations: every operation restarts the clock. The clock runs from the
        /// tool's first operation until the one that completes the graph, so the time before the first operation and
        /// after the one that completes the graph are not limited. Must be at least 1. When not specified, the default
        /// is 10.
        /// </remarks>
        int? IdleTimeoutMin { get; }

        /// <summary>
        /// Turns on file access reporting for the tool, and logs every operation BuildXL handles and the response to it.
        /// </summary>
        bool? EnableProjectGraphVerboseLogging { get; }
    }
}
