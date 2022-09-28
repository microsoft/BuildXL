// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Nagling queue for processing data in batches based on the size or a time interval which allows awaiting
    /// final result
    /// </summary>
    public class ResultNagleQueue<T, TResult> : IDisposable
    {
        private readonly NagleQueue<(T item, TaskSourceSlim<TResult> completion)> _innerQueue;

        public int BatchSize => _innerQueue.BatchSize;

        /// <nodoc />
        public ResultNagleQueue(int maxDegreeOfParallelism, TimeSpan interval, int batchSize)
        {
            _innerQueue = NagleQueue<(T item, TaskSourceSlim<TResult> completion)>.CreateUnstarted(maxDegreeOfParallelism, interval, batchSize);
        }

        /// <summary>
        /// Start the nagle queue
        /// </summary>
        public void Start(Func<IReadOnlyList<T>, Task<IReadOnlyList<TResult>>> execute)
        {
            _innerQueue.Start(async items =>
            {
                // If the callback provided to a nagle queue fails,
                // the overall processing stops and the queue itself changes its internal state to "faulted" state
                // that can be observed when Dispose method is called.
                //
                // This is very problematic in this particular case,
                // because it means that the new items added to the queue would never be finished
                // and all 'await EnqueueAsync(item)' will be blocked forever.
                //
                // To avoid this problem this operation catches all the errors and sets the exceptions into
                // the underlying task sources.
                try
                {
                    var bulkTask = execute(items.SelectList(b => b.item));
                    for (int i = 0; i < items.Length; i++)
                    {
                        var item = items[i];
                        item.completion.LinkToTask(bulkTask, i, (bulkResult, index) => bulkResult[index]);
                    }

                    await bulkTask;
                }
                catch (Exception e)
                {
                    foreach (var item in items)
                    {
                        item.completion.TrySetException(e);
                    }
                }
            });
        }

        /// <summary>
        /// Enqueue the item and await its result
        /// </summary>
        public Task<TResult> EnqueueAsync(T item)
        {
            var taskSource = TaskSourceSlim.Create<TResult>();
            _innerQueue.Enqueue((item, taskSource));
            return taskSource.Task;
        }

        /// <nodoc />
        public void Dispose()
        {
            _innerQueue.Dispose();
        }
    }
}
