// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Utilities.ParallelAlgorithms
{
    /// <summary>
    /// Wrapper of <see cref="ActionBlockSlim{T}"/> allowing running generic synchronous and asynchronous actions with limited
    /// parallelism and awaiting the results individually.
    /// </summary>
    public sealed class ActionQueue
    {
        private readonly ActionBlockSlim<Func<Task>> m_actionBlock;

        /// <inheritdoc cref="ActionBlockSlim{T}.PendingWorkItems"/>.
        public int PendingWorkItems => m_actionBlock.PendingWorkItems;

        /// <inheritdoc cref="ActionBlockSlim{T}.Complete"/>
        public void Complete() => m_actionBlock.Complete();

        /// <inheritdoc cref="ActionBlockSlim{T}.CompletionAsync"/>
        public Task CompletionAsync() => m_actionBlock.CompletionAsync();

        /// <nodoc />
        public ActionQueue(int degreeOfParallelism, int? capacityLimit = null)
        {
            m_actionBlock = ActionBlockSlim.CreateWithAsyncAction<Func<Task>>(degreeOfParallelism, static f => f(), capacityLimit);
        }

        /// <summary>
        /// Runs the delegate asynchronously for all items and returns the completion
        /// </summary>
        public Task ForEachAsync<T>(IEnumerable<T> items, Func<T, int, Task> body)
        {
            var tasks = new List<Task>();

            int index = 0;
            foreach (var item in items)
            {
                var itemIndex = index;
                tasks.Add(RunAsync(() =>
                {
                    return body(item, itemIndex);
                }));
                index++;
            }

            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Runs the delegate asynchronously for all items and returns the completion
        /// </summary>
        public Task<TResult[]> SelectAsync<T, TResult>(IEnumerable<T> items, Func<T, int, Task<TResult>> body)
        {
            var tasks = new List<Task<TResult>>();

            int index = 0;
            foreach (var item in items)
            {
                var itemIndex = index;
                tasks.Add(RunAsync(() =>
                {
                    return body(item, itemIndex);
                }));
                index++;
            }

            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Runs the delegate asynchronously and returns the completion
        /// </summary>
        public Task<T> RunAsync<T>(Func<T> func)
        {
            return RunAsync(() =>
            {
                var result = func();
                return Task.FromResult<T>(result);
            });
        }

        /// <summary>
        /// Runs the delegate asynchronously and returns the completion
        /// </summary>
        /// <exception cref="ActionBlockIsFullException">If the queue is full and the queue was configured to limit the queue size.</exception>
        public Task RunAsync(Action action)
        {
            return RunAsync(() =>
            {
                action();
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Runs the delegate asynchronously and returns the completion
        /// </summary>
        /// <exception cref="ActionBlockIsFullException">If the queue is full and the queue was configured to limit the queue size.</exception>
        public Task RunAsync(Func<Task> runAsync)
        {
            return RunAsync(async () =>
            {
                await runAsync();
                return Unit.Void;
            });
        }

        /// <summary>
        /// Runs the delegate asynchronously and returns the completion
        /// </summary>
        /// <exception cref="ActionBlockIsFullException">If the queue is full and the queue was configured to limit the queue size.</exception>
        public Task<T> RunAsync<T>(Func<Task<T>> runAsync)
        {
            var taskSource = TaskSourceSlim.Create<T>();

            m_actionBlock.Post(async () =>
            {
                try
                {
                    var task = runAsync();
                    taskSource.LinkToTask(task);
                    await task;
                }
                catch (Exception ex)
                {
                    // Still need to call TrySetException, because runAsync may fail synchronously.
                    taskSource.TrySetException(ex);
                }
            });

            return taskSource.Task;
        }
    }
}
