// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.ParallelAlgorithms
{
    /// <summary>
    /// Action block that allows for waiting on the result of the action.
    /// </summary>
    public class ResultBasedActionBlockSlim<T>
    {
        /// <summary>
        /// Items to be processed by the action block.
        /// </summary>
        private readonly struct QueueItem
        {
            private readonly TaskCompletionSource<T> m_tsc;

            internal Func<Task<T>> Func { get; }

            internal readonly Task<T> Completion => m_tsc.Task;

            internal QueueItem(Func<Task<T>> evalFunc)
            {
                Func = evalFunc;
                m_tsc = new TaskCompletionSource<T>();
            }

            internal async Task ExecuteAsync()
            {
                try
                {
                    var result = await Func();
                    m_tsc.SetResult(result);
                }
                catch (Exception e)
                {
                    m_tsc.SetException(e);
                }
            }
        }

        private readonly ActionBlockSlim<QueueItem> m_queue;

        /// <summary>
        /// Constructor.
        /// </summary>
        public ResultBasedActionBlockSlim(int degreeOfParallelism, CancellationToken cancellationToken)
        {
            m_queue = ActionBlockSlim.CreateWithAsyncAction<QueueItem>(
                degreeOfParallelism,
                item => item.ExecuteAsync(),
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Processes the given function asynchronously.
        /// </summary>
        public Task<T> ProcessAsync(Func<Task<T>> func)
        {
            var item = new QueueItem(func);
            m_queue.Post(item);
            return item.Completion;
        }
    }
}
