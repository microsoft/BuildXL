// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BuildXL.Ide.JsonRpc;

namespace BuildXL.Ide.LanguageServer.Providers
{
    /// <summary>
    /// Special helper class responsible for progress notification every half a second.
    /// </summary>
    internal sealed class ProgressNotifier : IDisposable
    {
        private readonly IProgressReporter m_progressReporter;
        private readonly int m_numberOfFiles;
        private int m_numberOfFoundReferences;
        private int m_numberOfProcessedFiles;
        private static readonly TimeSpan s_interval = TimeSpan.FromMilliseconds(500);
        private readonly System.Threading.Timer m_timer;
        private readonly object m_timerSyncRoot = new object();
        private readonly Stopwatch m_stopWatch;

        public ProgressNotifier(IProgressReporter progressReporter, int numberOfFiles, CancellationToken token)
        {
            m_progressReporter = progressReporter;
            m_numberOfFiles = numberOfFiles;
            m_timer = new Timer(Handler, null, s_interval, s_interval);
            m_stopWatch = Stopwatch.StartNew();

            token.Register(
                () =>
                {
                    // Protect the access to the timer to avoid the race condition with the disposal.
                    lock (m_timerSyncRoot)
                    {
                        m_timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    }
                });
        }

        public void ProcessFileReferences(List<ReferencedSymbol> references)
        {
            Interlocked.Increment(ref m_numberOfProcessedFiles);
            Interlocked.Add(ref m_numberOfFoundReferences, references.Sum(r => r.References.Count));
        }

        public void Dispose()
        {
            lock (m_timerSyncRoot)
            {
                m_timer.Dispose();
            }
        }

        private void Handler(object unused)
        {
            int numberOfReferences = Volatile.Read(ref m_numberOfFoundReferences);
            int numberOfProcessedFiles = Volatile.Read(ref m_numberOfProcessedFiles);
            m_progressReporter.ReportFindReferencesProgress(FindReferenceProgressParams.Create(numberOfReferences, (int)m_stopWatch.ElapsedMilliseconds, numberOfProcessedFiles, m_numberOfFiles));
        }
    }
}
