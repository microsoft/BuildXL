// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Interface for collecting files to be materialized/pre-rendered in remote agents.
    /// </summary>
    public interface IRemoteFilePredictor
    {
        /// <summary>
        /// Gets input prediction for a process
        /// </summary>
        /// <param name="process">Process pip.</param>
        /// <returns>Files that are predicted to be the inputs of the process.</returns>
        /// <remarks>
        /// The predicted input files may not just be direct dependencies of the process. Those files may be specified as
        /// part of a sealed directory that the process depends on. These files can be used by the remoting engine
        /// to prepopulate the cache on the agent where the process is going to execute. Also, these files can be used
        /// to perform static virtual file system pre-rendering.
        /// 
        /// In the future, we may want to include as well the content hashes of the files. However, there is no
        /// guarantee that BuildXL will use the same hash type as the remoting engine.
        /// </remarks>
        Task<IEnumerable<AbsolutePath>> GetInputPredictionAsync(Process process);
    }
}
