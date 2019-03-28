// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;

namespace TypeScript.Net.Diagnostics
{
    /// <summary>
    /// Collection of diagnostics.
    /// </summary>
    /// <remarks>
    /// Implementation ported from createDiagnosticCollection in utilities.ts
    /// </remarks>
    public sealed class DiagnosticCollection : IDiagnosticCollection
    {
        private List<Diagnostic> m_nonFileDiagnostics = new List<Diagnostic>();
        private Dictionary<string, List<Diagnostic>> m_fileDiagnostics = new Dictionary<string, List<Diagnostic>>();

        private bool m_diagnosticsModified;
        private int m_modificationCount;

        /// <inheritdoc />
        public int GetModificationCount()
        {
            return m_modificationCount;
        }

        /// <inheritdoc />
        public void Add(Diagnostic diagnostic)
        {
            var diagnostics =
                diagnostic.File != null
                    ? m_fileDiagnostics.GetOrAddAtomic(diagnostic.File.FileName, _ => new List<Diagnostic>())
                    : Volatile.Read(ref m_nonFileDiagnostics);

            diagnostics.AddAtomic(diagnostic);

            Volatile.Write(ref m_diagnosticsModified,  true);
            Interlocked.Increment(ref m_modificationCount);
        }

        /// <inheritdoc />
        public List<Diagnostic> GetGlobalDiagnostics()
        {
            SortAndDeduplicateIfNeeded();
            return m_nonFileDiagnostics;
        }

        /// <inheritdoc />
        public List<Diagnostic> GetDiagnostics(string fileName = null)
        {
            SortAndDeduplicateIfNeeded();

            if (fileName != null)
            {
                return m_fileDiagnostics.GetOrAddAtomic(fileName, _ => new List<Diagnostic>());
            }

            List<Diagnostic> allDiagnostics = new List<Diagnostic>(m_nonFileDiagnostics);

            lock (m_fileDiagnostics)
            {
                foreach (var fileDiagnostic in m_fileDiagnostics.Values)
                {
                    allDiagnostics.AddRange(fileDiagnostic);
                }
            }

            return DiagnosticUtilities.SortAndDeduplicateDiagnostics(allDiagnostics);
        }

        private void SortAndDeduplicateIfNeeded()
        {
            if (!Volatile.Read(ref m_diagnosticsModified))
            {
                return;
            }

            Volatile.Write(ref m_diagnosticsModified, false);
            m_nonFileDiagnostics = DiagnosticUtilities.SortAndDeduplicateDiagnostics(m_nonFileDiagnostics);

            Map<List<Diagnostic>> sortedAndDeduplicatedFileDiagnostics = new Map<List<Diagnostic>>();
            lock (m_fileDiagnostics)
            {
                foreach (var key in m_fileDiagnostics.Keys)
                {
                    sortedAndDeduplicatedFileDiagnostics[key] = DiagnosticUtilities.SortAndDeduplicateDiagnostics(m_fileDiagnostics[key]);
                }

                m_fileDiagnostics = sortedAndDeduplicatedFileDiagnostics;
            }
        }
    }
}
