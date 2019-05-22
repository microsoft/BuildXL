// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Configuration for shim execution in place of child processes executing in a build sandbox.
    /// </summary>
    public sealed class SubstituteProcessExecutionInfo
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public SubstituteProcessExecutionInfo(AbsolutePath substituteProcessExecutionShimPath, bool shimAllProcesses, IReadOnlyCollection<ShimProcessMatch> processMatches)
        {
            Contract.Requires(substituteProcessExecutionShimPath.IsValid);

            SubstituteProcessExecutionShimPath = substituteProcessExecutionShimPath;
            ShimAllProcesses = shimAllProcesses;
            ShimProcessMatches = processMatches ?? Array.Empty<ShimProcessMatch>();
        }
        
        /// <summary>
        /// Children of the Detoured process are not directly executed,
        /// but instead this substitute shim process is injected instead, with
        /// the original process's command line appended.
        /// </summary>
        public AbsolutePath SubstituteProcessExecutionShimPath { get; set; }

        /// <summary>
        /// Specifies the shim injection mode. When true, <see cref="ShimProcessMatches"/>
        /// specifies the processes that should not be shimmed. When false, ShimProcessMatches
        /// specifies the processes that should be shimmed.
        /// </summary>
        public bool ShimAllProcesses { get; }

        /// <summary>
        /// Processes that should or should not be be shimmed, according to the shim injection mode specified
        /// by <see cref="ShimAllProcesses"/>.
        /// </summary>
        public IReadOnlyCollection<ShimProcessMatch> ShimProcessMatches { get; }
    }

    /// <summary>
    /// Process matching information used for including or excluding processes in <see cref="SubstituteProcessExecutionInfo"/>.
    /// </summary>
    /// <remarks>In unmanaged code this is decoded into class ShimProcessMatch in DetoursHelpers.cpp.</remarks>
    public sealed class ShimProcessMatch
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public ShimProcessMatch(PathAtom processName, PathAtom argumentMatch)
        {
            Contract.Requires(processName.IsValid);

            ProcessName = processName;
            ArgumentMatch = argumentMatch;
        }

        /// <summary>
        /// A process name to match, including extension. E.g. "MSBuild.exe".
        /// </summary>
        public PathAtom ProcessName { get; }

        /// <summary>
        /// An optional string to match in the arguments of the process to further refine the match.
        /// Can be used for example with ProcessName="node.exe" ArgumentMatch="gulp.js" to match the
        /// Gulp build engine process.
        /// </summary>
        public PathAtom ArgumentMatch { get; }
    }
}
