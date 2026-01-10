// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Processes
{
    /// <summary>
    /// Represents a collection of explicitly reported file accesses.
    /// </summary>
    /// <remarks>
    /// By abstracting the collection of explicitly reported file accesses behind this interface, we can process them
    /// as they come into <see cref="SandboxedProcessReports"/> and facilitate post processing by not waiting till
    /// all the accesses are reported to start.
    /// </remarks>
    public interface IExplicitlyReportedAccesses
    {
        /// <nodoc/>
        void Add(ReportedFileAccess reportedFileAccess);

        /// <nodoc/>
        void Remove(ReportedFileAccess reportedFileAccess);

        /// <nodoc/>
        ISet<ReportedFileAccess> ExplicitlyReportedFileAccesses();
    }
}
