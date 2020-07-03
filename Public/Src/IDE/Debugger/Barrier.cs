// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// A simple barrier class on which many threads can wait.
    ///
    /// Strongly immutable.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:implement disposable")]
    public sealed class Barrier
    {
        private readonly ManualResetEventSlim m_signalEvent;

        /// <summary>
        /// Creates a barrier.  Upon creation, the barrier is closed.
        /// </summary>
        public Barrier()
        {
            m_signalEvent = new ManualResetEventSlim(false);
        }

        /// <summary>
        /// Blocks the caller thread until the barrier is open (if already open, nothing happens).
        /// </summary>
        public void Wait()
        {
            m_signalEvent.Wait();
        }

        /// <summary>
        /// Opens the barrier.  Once open, cannot be closed again.
        /// </summary>
        public void Signal()
        {
            m_signalEvent.Set();
        }
    }
}
