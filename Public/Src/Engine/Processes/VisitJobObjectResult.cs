// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Processes
{
    /// <summary>
    /// Return status for VisitJobObjectProcesses operation
    /// </summary>
    public enum VisitJobObjectResult : byte
    {
        /// <summary>
        /// Process was terminated before starting the visitation 
        /// </summary>
        TerminatedBeforeVisitation,

        /// <summary>
        /// Success
        /// </summary>
        Success,

        /// <summary>
        /// The operation failed
        /// </summary>
        Failed,
    }
}
