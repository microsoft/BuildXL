// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Reports progress
    /// </summary>
    internal sealed class ProgressReporter : IDisposable
    {
        public delegate void Report(int doneCount, int total);

        private Timer m_timer;
        public int DoneCount;

        public ProgressReporter(int total, Report report)
        {
            m_timer = new Timer(
                            o =>
                            {
                                report(DoneCount, total);
                            },
                            null,
                            5000,
                            5000);
        }

        public void Dispose()
        {
            if (m_timer != null)
            {
                m_timer.Change(0, Timeout.Infinite);

                // kill and wait for the status timer to die...
                using (var e = new AutoResetEvent(false))
                {
                    m_timer.Dispose(e);
                    e.WaitOne();
                }

                m_timer = null;
            }
        }
    }
}
