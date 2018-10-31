// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
