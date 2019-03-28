// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Task scheduler for executing tasks on a dedicated set of threads
    /// </summary>
    public sealed class DedicatedThreadsTaskScheduler : TaskScheduler, IDisposable
    {
        private readonly BlockingCollection<Task> _tasks = new BlockingCollection<Task>();
        private readonly List<Thread> _threads;

        /// <summary>
        /// Creates a new task scheduler
        /// </summary>
        /// <param name="threadCount">the number of dedicated threads</param>
        /// <param name="baseThreadName">the base name to use when naming the dedicated threads</param>
        public DedicatedThreadsTaskScheduler(int threadCount, string baseThreadName = null)
        {
            Contract.Requires(threadCount > 0);

            _threads = Enumerable.Range(0, threadCount).Select(i =>
            {
                Thread t = new Thread(() =>
                {
                    foreach (var task in _tasks.GetConsumingEnumerable())
                    {
                        TryExecuteTask(task);
                    }
                });

                if (baseThreadName != null)
                {
                    t.Name = $"{baseThreadName} {i}";
                }

                t.IsBackground = true;
                t.Start();
                return t;

            }).ToList();
        }

        /// <inheritdoc />
        protected override void QueueTask(Task task)
        {
            _tasks.Add(task);
        }

        /// <inheritdoc />
        public override int MaximumConcurrencyLevel
        {
            get
            {
                return _threads.Count;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _tasks.CompleteAdding();
        }

        /// <inheritdoc />
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }

        /// <inheritdoc />
        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return _tasks;
        }
    }
}
