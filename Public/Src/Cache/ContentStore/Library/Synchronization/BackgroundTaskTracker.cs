// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Synchronization
{
    /// <summary>
    ///     Use an instance of this class to handle any asynchronous tasks that only
    ///     need to complete by the end of the lifetime of the owning object.
    /// </summary>
    public sealed class BackgroundTaskTracker : IShutdown<ValueUnit>
    {
        private static readonly Type Type = typeof(BackgroundTaskTracker);
        private readonly Task _handlerTask;
        private readonly ConcurrentQueue<QueueItem> _backgroundTaskQueue = new ConcurrentQueue<QueueItem>();
        private readonly Tracer _tracer = new Tracer(nameof(BackgroundTaskTracker));

        /// <summary>
        ///     This event controls the background processing and avoids busy waiting on tasks. Every addition
        ///     of items to the task queue, or the shutdown event, signal to the background processing that
        ///     there is work to be done.
        /// </summary>
        private readonly AutoResetEvent _hasTasksToProcessEvent = new AutoResetEvent(false);

        /// <summary>
        ///     This lock controls access to the background task queue and avoids a race where shutdown
        ///     causes items just added to be missed during final processing flush.
        ///     If needed, this lock can be replaced with Async version of the lock to avoid unnecessary blocking,
        ///     or perhaps with ConcurrentExclusiveSchedulerPair paradigm.
        /// </summary>
        private readonly ReaderWriterLockSlim _queueLock = new ReaderWriterLockSlim();

        private readonly string _logPrefix;
        private readonly Context _context;
        private readonly bool _logTasks;
        private volatile bool _inShutdown;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackgroundTaskTracker"/> class.
        ///     Construct a new instance.
        /// </summary>
        /// <param name="name">String name associated with the instance.</param>
        /// <param name="context">Context to receive trace execution.</param>
        /// <param name="logTasks">If true, logging of task await start/stop is performed.</param>
        public BackgroundTaskTracker(string name, Context context, bool logTasks = false)
        {
            _inShutdown = false;
            _logPrefix = string.Format(CultureInfo.CurrentCulture, "{0}[{1}]", Type.Name, name);
            _context = context;
            _logTasks = logTasks;
            _handlerTask = Task.Run(HandleBackgroundTasks);
        }

        /// <summary>
        ///     Gets a value indicating whether it is safe to call the Add() methods.
        /// </summary>
        /// <remarks>
        ///     Is initially true and remains true until ShutdownAsync() or Dispose() is called.
        /// </remarks>
        public bool InShutdown => _inShutdown;

        /// <summary>
        ///     Waits for all added tasks to finish and cleans up resources.
        /// </summary>
        public void Dispose()
        {
            if (!ShutdownStarted)
            {
                _tracer.Error(_context, $"{GetType().Name} must be shutdown before Disposing.");
            }

            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _hasTasksToProcessEvent.Dispose();
            _queueLock.Dispose();
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public async Task<ValueUnit> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;

            // Lock the queue adds out while _inShutdown is being set. This will ensure that
            // any adds that happen after this point will have to observe _inShutdown being set.
            _queueLock.EnterWriteLock();

            try
            {
                _inShutdown = true;
            }
            finally
            {
                _queueLock.ExitWriteLock();
            }

            _hasTasksToProcessEvent.Set();
            await _handlerTask;
            ShutdownCompleted = true;
            return ValueUnit.Void;
        }

        private async Task ProcessQueueItem(string logPrefix)
        {
            // ReSharper disable once UnusedVariable
            bool itemDequeued = _backgroundTaskQueue.TryDequeue(out var queueItem);
            Contract.Assert(itemDequeued);

            if (queueItem.SyncTask.IsValid)
            {
                _tracer.Diagnostic(_context, $"{logPrefix} received SyncTask {queueItem.SyncTask.Task.Id} ({queueItem.Tag})");

                // Avoid getting blocked by ending up in a blocking wait. This can happen because TaskCompletionSource
                // SetResult runs on the active thread and can unblock other waiting tasks and resume their async
                // calls. Once such case, with multiple interacting BackgroundThreadTracker objects, causes the active
                // thread to end up in the blocking GetConsumingEnumerable() of another instance. So, set the result
                // in another thread and move on here immediately. The task created here is not tracked.
                TaskSourceSlim<ValueUnit> tcs = queueItem.SyncTask;
                tcs.SetResult(ValueUnit.Void);
            }
            else
            {
                try
                {
                    if (_logTasks)
                    {
                        _tracer.Diagnostic(_context, $"{logPrefix} await task {queueItem.Task.Id} ({queueItem.Tag})");
                    }

                    await queueItem.Task;

                    if (_logTasks)
                    {
                        _tracer.Diagnostic(_context, $"{logPrefix} await task {queueItem.Task.Id} ({queueItem.Tag}) complete");
                    }
                }
                catch (Exception exception)
                {
                    _tracer.Debug(_context, $"{logPrefix} ignoring exception in task {queueItem.Task.Id}: {exception}");
                }
            }
        }

        private async Task HandleBackgroundTasks()
        {
            var logPrefix = $"{_logPrefix}.HandleBackgroundTasks";

            while (true)
            {
                if (InShutdown)
                {
                    // If InShutdown is set, that means that it was set in ShutDownAsync while holding a write lock. That
                    // ensures that InShutdown is now visible to all threads, due to a mandatory RWL synchronization point
                    // at the lock exit. It also ensures that no Add (and hence Enqueue) will be able to proceed from that
                    // point on, allowing this InShutdown check to be a simple check of a volatile variable. Also, since
                    // ShutDownAsync signals the _hasTasksToProcessEvent, that also ensures that there is no deadlock possible.
                    while (!_backgroundTaskQueue.IsEmpty)
                    {
                        await ProcessQueueItem(logPrefix);
                    }

                    break;
                }

                if (!_backgroundTaskQueue.IsEmpty)
                {
                    await ProcessQueueItem(logPrefix);
                }
                else
                {
                    // Instead of polling and busy-waiting, use the event to pause this thread until there is work to be done.
                    // Both queue add and shutdown signal this event, so there is no chance of deadlock.
                    await Task.Factory.StartNew(
                        () =>
                        {
                            try
                            {
                                _hasTasksToProcessEvent.WaitOne();
                            }
                            catch (ObjectDisposedException)
                            {
                                // If there is a catastrophic exception elsewhere, then this event might be disposed before
                                // Shutdown has been called.
                                _tracer.Diagnostic(_context, $"{_logPrefix} could not signal event as it has been disposed.");
                            }
                        },
                        TaskCreationOptions.LongRunning);
                }
            }

            _tracer.Diagnostic(_context, $"{logPrefix} exit");
        }

        /// <summary>
        ///     Adds a task to the collection of tasks that will complete by the time Dispose() returns.
        /// </summary>
        public void Add(Func<Task> backgroundTaskFunc, string tag = null)
        {
            Contract.Requires(backgroundTaskFunc != null);
            Add(Task.Run(backgroundTaskFunc), tag);
        }

        /// <summary>
        ///     Adds a task to the collection of tasks that will complete by the time Dispose() returns.
        /// </summary>
        public void Add(Task backgroundTask, string tag = null)
        {
            Contract.Requires(backgroundTask != null);

            Enqueue(new QueueItem(backgroundTask, tag), "Add", backgroundTask.Id, tag);
        }

        /// <summary>
        ///     Waits for all currently added background tasks to complete.
        /// </summary>
        public Task Synchronize(string tag = null)
        {
            var syncCompletion = TaskSourceSlim.Create<ValueUnit>();

            Enqueue(new QueueItem(syncCompletion, tag), "Synchronize", syncCompletion.Task.Id, tag);

            return syncCompletion.Task;
        }

        /// <summary>
        ///     Put the queue item in the queue and signal that the queue is non-empty
        /// </summary>
        /// <param name="qi">Queue item</param>
        /// <param name="taskName">Name of the task for debugging purposes</param>
        /// <param name="taskId">Id of the task for debugging purposes</param>
        /// <param name="tag">Tag used for the task</param>
        private void Enqueue(QueueItem qi, string taskName, int taskId, string tag)
        {
            // It is OK to enter this lock synchronously here, because the only time we will actually wait
            // is if write lock is taken by shutdown, so enqueue would have been wasted anyway.
            _queueLock.EnterReadLock();

            try
            {
                // This contract below is a contract between provider of Add API and its consumers that
                // says that no one is allowed to add any tasks when background queue is in the process
                // of shutting down. Since both Add and Shutdown are in control of the user, this
                // synchronization is done completely in user realm, by having to wait for Synchronize
                // call to complete before shutting down. We simple enforce the contract here.
                if (InShutdown)
                {
                    var message = string.Format(
                        CultureInfo.InvariantCulture, "{0} Cannot {1} as shutdown has already begun.", _logPrefix, taskName);
                    throw new InvalidOperationException(message);
                }

                _backgroundTaskQueue.Enqueue(qi);
                if (_logTasks)
                {
                    _tracer.Diagnostic(_context, $"{_logPrefix}.{taskName} task {taskId} ({tag})");
                }

                _hasTasksToProcessEvent.Set();
            }
            finally
            {
                _queueLock.ExitReadLock();
            }
        }

        private readonly struct QueueItem
        {
            public readonly TaskSourceSlim<ValueUnit> SyncTask;
            public readonly Task Task;
            public readonly string Tag;

            public QueueItem(Task task, string tag)
                : this()
            {
                Task = task;
                SyncTask = default;
                Tag = tag;
            }

            public QueueItem(TaskSourceSlim<ValueUnit> syncTask, string tag)
                : this()
            {
                Task = null;
                SyncTask = syncTask;
                Tag = tag;
            }
        }
    }
}
