// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Action that fires repeatedly according to the specified interval on a cancellable dedicated thread.
    /// </summary>
    /// <remarks>
    /// Using a dedicated thread can prevent the action from affected by potential threadpool exhaustion issues.
    /// </remarks>
    public class CancellableTimedAction : IDisposable
    {
        private readonly CancellationTokenSource m_cancellationTokenSource;
        private int m_started = 0;
        private readonly Thread m_thread;
        private readonly Action m_callback;

        /// <summary>
        /// Creates an instance of <see cref="CancellableTimedAction"/>.
        /// </summary>
        public CancellableTimedAction(Action callback, int intervalMs, string name = null)
        {
            m_cancellationTokenSource = new CancellationTokenSource();
            m_callback = callback;
            m_thread = new Thread(() => Loop(callback, intervalMs, m_cancellationTokenSource.Token));
            if (name != null)
            {
                m_thread.Name = name;
            }
        }

        /// <summary>
        /// Starts the thread.
        /// </summary>
        /// <param name="syncStart">
        /// Whether the first call to callback method is synchronous. Default is set to false.
        /// </param>
        public bool Start(bool syncStart = false)
        {
            if (Interlocked.CompareExchange(ref m_started, 1, 0) == 0)
            {
                if (syncStart)
                {
                    m_callback();
                }

                m_thread.Start();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Cancels the thread.
        /// </summary>
        public void Cancel()
        {
            m_cancellationTokenSource.Cancel();
        }

        /// <summary>
        /// Joins the thread.
        /// </summary>
        public void Join()
        {
            if (m_thread.IsAlive)
            {
                m_thread.Join();
            }
        }

        private static void Loop(Action callback, int intervalMs, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                callback();
                bool cancelled = token.WaitHandle.WaitOne(intervalMs);
                if (cancelled)
                {
                    break;
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_cancellationTokenSource.Dispose();
        }
    }
}
