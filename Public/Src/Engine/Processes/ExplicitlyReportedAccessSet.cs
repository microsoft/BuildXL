// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Processes
{
    /// <summary>
    /// A simple set backing up the implementation of <see cref="IExplicitlyReportedAccesses"/>.
    /// </summary>
    /// <remarks>
    /// Mostly used for testing or cases outside the main pip execution engine.
    /// </remarks>
    public class ExplicitlyReportedAccessSet : IExplicitlyReportedAccesses
    {
        private readonly HashSet<ReportedFileAccess> m_reportedFileAccesses = [];

        /// <inheritdoc/>
        public void Add(ReportedFileAccess reportedFileAccess)
        {
            m_reportedFileAccesses.Add(reportedFileAccess);
        }

        /// <inheritdoc/>
        public void Remove(ReportedFileAccess reportedFileAccess)
        {
            m_reportedFileAccesses.Remove(reportedFileAccess);
        }

        /// <inheritdoc/>
        public ISet<ReportedFileAccess> ExplicitlyReportedFileAccesses() => m_reportedFileAccesses;
    }
}
