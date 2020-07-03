// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Processes
{
    /// <summary>
    /// Logger for monitoring of a sandboxed process
    /// </summary>
    public interface ISandboxedProcessLogger
    {
        /// <summary>
        /// Logs the reported processes and the reported file accesses
        /// </summary>
        void LogProcessObservation(
            IReadOnlyCollection<ReportedProcess> processes,
            IReadOnlyCollection<ReportedFileAccess> fileAccesses,
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses);
    }
}
