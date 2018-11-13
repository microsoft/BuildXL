// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A m_timer that can wait (synchronously or asynchronously) for any m_running callbacks to complete when it is stopped
    /// </summary>
    /// <remarks>
    /// Multiple calls to the timer callback will never happen concurrently for the same timer instance. Also the
    /// underlying timer is only rescheduled after the action has been completed. This prevents the timer from having
    /// a piling up effect.
    /// </remarks>
    public sealed class StoppableTimer : IDisposable
    {
        private readonly Timer m_timer;
        private readonly Action m_callback;
        private readonly int m_period;
        private readonly object m_lockObj = new object();
        private bool m_running;

        /// <summary>
        ///  Initializes a new instance of the StoppableTimer class.
        /// </summary>
        /// <param name="callback">An action to be executed</param>
        /// <param name="dueTime">The amount of time to delay before m_callback is invoked, in milliseconds. Specify
        /// System.Threading.Timeout.Infinite to prevent the m_timer from starting. Specify
        /// zero (0) to start the m_timer immediately.</param>
        /// <param name="period">The time interval between invocations of m_callback, in milliseconds. This does not
        /// account for the time it takes the callback to run. So if the callback takes 2 seconds, setting the period
        /// to 2 seconds will result in the callback method being called every 4 seconds.</param>
        public StoppableTimer(Action callback, int dueTime, int period)
        {
            m_callback = callback;

            // Period will be used to as the due time after timer fires. However, if period is 0, then the specified behavior is 
            // the callback is only invoked once if due time is not inifinite. To follow the specification, if period is 0, then it is set to infinite.
            m_period = period != 0 ? period : Timeout.Infinite;
            m_running = true;
            m_timer = new Timer(
                callback: s => ((StoppableTimer)s).FireTimer(),
                state: this,
                dueTime: Timeout.Infinite,
                // Only schedule the timer to be run when the action has been completed
                period: Timeout.Infinite);

            // Make sure m_timer is assigned before starting to make sure the callback cannot complete before
            // FireTimer would attemt to set the due time.
            m_timer.Change(dueTime: dueTime, period: Timeout.Infinite);
        }

        private void FireTimer()
        {
            lock (m_lockObj)
            {
                if (m_running)
                {
                    m_callback();
                    m_timer.Change(dueTime: m_period, period: Timeout.Infinite);
                }
            }
        }

        /// <summary>
        /// Changes the start time and the interval between <see cref="m_callback"/> invocations for <see cref="m_timer"/>.
        /// </summary>
        /// <param name="dueTime">The amount of time to delay before <see cref="m_callback"/> is invoked, in milliseconds. Specify
        /// <see cref="Timeout.Infinite"/> to prevent <see cref="m_timer"/> from starting. Specify
        /// zero (0) to start <see cref="m_timer"/> immediately.
        /// </param>
        /// <param name="period">The time interval between invocations of <see cref="m_callback"/>, in milliseconds.
        /// Specify <see cref="Timeout.Infinite"/> to disable periodic signaling.
        /// </param>
        /// <returns>true if the timer was successfully updated; otherwise, false.</returns>
        public bool Change(int dueTime, int period)
        {
            lock (m_lockObj)
            {
                return m_timer.Change(dueTime, period);
            }
        }

        /// <summary>
        /// Stops the m_timer and synchronously waits for any m_running callbacks to finish m_running
        /// </summary>
        public void Stop()
        {
            lock (m_lockObj)
            {
                // FireTimer is *not* m_running _callback (since we got the lock)
                m_timer.Change(
                    dueTime: Timeout.Infinite,
                    period: Timeout.Infinite);

                m_running = false;
            }

            // Now FireTimer will *never* run _callback
        }

        /// <summary>
        /// Stops the m_timer and returns a Task that will complete when any m_running callbacks finish m_running
        /// </summary>
        public Task StopAsync()
        {
            return Task.Factory.StartNew(
                action: s => ((StoppableTimer)s).Stop(),
                state: this,
                cancellationToken: CancellationToken.None,
                creationOptions: TaskCreationOptions.DenyChildAttach,
                scheduler: TaskScheduler.Default);
        }

        /// <summary>
        /// Stops the m_timer, waits for any callbacks to finish m_running, and disposes it
        /// </summary>
        public void Dispose()
        {
            Stop();
            m_timer.Dispose();
        }
    }
}
