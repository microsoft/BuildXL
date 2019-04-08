// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// This class is very similar to ActionBlock except that the enqueued actions can create themselves other actions.
    /// As a result the caller does not know when to call Complete.
    /// The solution is to keep a refcount of all queued or running items and to call Complete automatically
    /// when WhenDone() is requested and there are no more queued or running items.
    /// </summary>
    public sealed class RecursiveActionBlock<TInput>
    {
        private ActionBlock<TInput> m_actionBlock;
        private int m_queuedOrRunning;
        private int m_whenDoneCalled;   // Used to guard against multiple calls of WhenDone().

        /// <summary>
        /// Create a new recursive action block
        /// </summary>
        public RecursiveActionBlock(Func<TInput, Task> action, int maxDegreeOfParallelism)
        {
            Contract.Requires(action != null);
            Contract.Requires(maxDegreeOfParallelism >= 1);

            m_actionBlock = new ActionBlock<TInput>(
                async (item) =>
                {
                    try
                    {
                        await action(item);
                    }
                    finally
                    {
                        DecrementQueuedOrRunning();
                    }
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism,
                });
        }

        /// <summary>
        /// Enqueue an additional item. This method should be called only from inside the action.
        /// </summary>
        public void Enqueue(TInput item)
        {
            int oldQueuedOrRunning = Interlocked.Increment(ref m_queuedOrRunning);
            Contract.Assume(oldQueuedOrRunning >= 0, "Trying to call Enqueue outside of the seeded actions after WhenDone is called");

            m_actionBlock.Post(item);
        }

        /// <summary>
        /// Return the task to wait to for completion.
        /// </summary>
        public Task WhenDone()
        {
            if (Interlocked.Exchange(ref m_whenDoneCalled, 1) == 0)
            {
                DecrementQueuedOrRunning();
            }

            return m_actionBlock.Completion;
        }

        private void DecrementQueuedOrRunning()
        {
            if (Interlocked.Decrement(ref m_queuedOrRunning) < 0)
            {
                m_actionBlock.Complete();
            }
        }
    }
}
