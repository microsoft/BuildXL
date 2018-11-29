// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Configuration for shim execution in place of child processes of build engines
    /// executing in an AnyBuild context.
    /// </summary>
    public sealed class AnyBuildShimInfo
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public AnyBuildShimInfo(AbsolutePath substituteProcessExecutionShimPath)
        {
            Contract.Requires(substituteProcessExecutionShimPath.IsValid);
            SubstituteProcessExecutionShimPath = substituteProcessExecutionShimPath;
        }
        
        /// <summary>
        /// Children of the Detoured process are not directly executed,
        /// but instead this substitute shim process is injected instead, with
        /// the original process's command line appended.
        /// </summary>
        public AbsolutePath SubstituteProcessExecutionShimPath { get; set; }
    }
}
