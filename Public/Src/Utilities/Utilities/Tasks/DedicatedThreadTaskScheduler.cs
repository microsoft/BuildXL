// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Task scheduler for executing tasks on a dedicated thread
    /// </summary>
    public sealed class DedicatedThreadTaskScheduler : TaskScheduler, IDisposable
    {
        /// <summary>
        /// Whether the current thread belongs to the task scheduler
        /// </summary>
        [ThreadStatic]
        private static bool s_isDedicatedThread;

        private ConcurrentQueue<Task> _tasks = new ConcurrentQueue<Task>();

        private bool _isDisposed;
        private int _pendingTaskCount;

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _taskSignal = new ManualResetEventSlim();

        /// <summary>
        /// Count of pending tasks
        /// </summary>
        public int PendingTaskCount => _pendingTaskCount;

        /// <summary>
        /// Creates a new task scheduler
        /// </summary>
        /// <param name="threadName">the base name to use when naming the dedicated threads</param>
        public DedicatedThreadTaskScheduler(string threadName = null)
        {
            _thread = new Thread(() =>
            {
                RunDedicatedThread();
            });

            if (threadName != null)
            {
                _thread.Name = threadName;
            }

            _thread.IsBackground = true;
            _thread.Start();
        }

        private void RunDedicatedThread()
        {
            s_isDedicatedThread = true;

            while (!_isDisposed)
            {
                _taskSignal.Reset();

                while (_tasks.TryDequeue(out var task))
                {
                    Interlocked.Decrement(ref _pendingTaskCount);
                    TryExecuteTask(task);

                    if (_isDisposed)
                    {
                        return;
                    }
                }

                _taskSignal.Wait();
            }
        }

        /// <inheritdoc />
        protected override void QueueTask(Task task)
        {
            Interlocked.Increment(ref _pendingTaskCount);
            _tasks.Enqueue(task);
            _taskSignal.Set();
        }

        /// <inheritdoc />
        public override int MaximumConcurrencyLevel
        {
            get
            {
                return 1;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _isDisposed = true;
        }

        /// <inheritdoc />
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (s_isDedicatedThread)
            {
                return TryExecuteTask(task);
            }

            return false;
        }

        /// <inheritdoc />
        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return _tasks;
        }
    }
}
