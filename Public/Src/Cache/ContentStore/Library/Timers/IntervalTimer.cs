// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Timers
{
    /// <summary>
    /// Triggers an action run in the background at a provided interval.
    /// </summary>
    public class IntervalTimer : IDisposable
    {
        private const int MonitorTimeoutMilliseconds = 2000;

        private readonly SemaphoreSlim _monitor = new SemaphoreSlim(1, 1);
        private readonly Func<Task> _actionFunc;
        private readonly Action<string> _logAction;
        private readonly Timer _interval;

        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="IntervalTimer"/> class.
        /// </summary>
        /// <param name="actionFunc">Asynchronous action performed at interval.</param>
        /// <param name="period">Interval between triggered actions.</param>
        /// <param name="logAction">Action to optionally log events related to the action triggering.</param>
        /// <param name="dueTime">Initial wait time before triggering action.</param>
        public IntervalTimer(Func<Task> actionFunc, TimeSpan period, Action<string> logAction = null, TimeSpan? dueTime = null)
        {
            Contract.Requires(actionFunc != null);

            _actionFunc = actionFunc;
            _logAction = logAction;
            _interval = new Timer(
                async sender => await TriggerActionAsync(),
                null,
                dueTime ?? TimeSpan.FromMinutes(0),
                period);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntervalTimer"/> class.
        /// </summary>
        /// <param name="action">Action performed at interval.</param>
        /// <param name="period">Interval between triggered actions.</param>
        /// <param name="logAction">Action to optionally log events related to the action triggering.</param>
        /// <param name="dueTime">Initial wait time before triggering action.</param>
        public IntervalTimer(Action action, TimeSpan period, Action<string> logAction = null, TimeSpan? dueTime = null)
            : this(
                () =>
                {
                    action();
                    return Task.FromResult(0);
                },
                period,
                logAction,
                dueTime)
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _monitor.Wait(MonitorTimeoutMilliseconds);
                try
                {
                    _interval.Dispose();
                    _disposed = true;
                }
                finally
                {
                    _monitor.Release();
                }

                _monitor.Dispose();
            }
        }

        private async Task TriggerActionAsync()
        {
            try
            {
                if (await _monitor.WaitAsync(MonitorTimeoutMilliseconds))
                {
                    try
                    {
                        if (!_disposed)
                        {
                            await _actionFunc();
                        }
                        else
                        {
                            _logAction?.Invoke("Not executing timer's action because the timer has been disposed.");
                        }
                    }
                    finally
                    {
                        _monitor.Release();
                    }
                }
                else
                {
                    _logAction?.Invoke("Not executing timer's action because another thread has the lock.");
                }
            }
            catch (Exception e)
            {
                _logAction?.Invoke($"Exception thrown while timer's action was triggered: {e}");
            }
        }
    }
}
