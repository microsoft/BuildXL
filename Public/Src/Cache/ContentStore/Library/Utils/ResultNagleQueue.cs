// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Extensions;
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
                var bulkTask = execute(items.SelectList(b => b.item));
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    item.completion.LinkToTask(bulkTask, i, (bulkResult, index) => bulkResult[index]);
                }

                await bulkTask;
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
