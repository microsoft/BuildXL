// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Central location that controls task scheduling for evaluation phase.
    /// </summary>
    /// <remarks>
    /// The main of this type right now is to limit the number of Task.Run in the codebase.
    /// All the logic that controls task scheduling for evaluation now in one place
    /// and we can make a more complicated scheduling just by changing one implementation.
    /// Today, this type just calls Task.Run because this produces the best performance (compared to other naive solutions, like using ActionBlock).
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    [SuppressMessage("Microsoft.Design", "CA1001:Implement IDisposable because of m_cancellationSource", Justification = "It doesn't matter if one CancellationTokenSource is not disposed")]
    public sealed class EvaluationScheduler : IEvaluationScheduler
    {
        private readonly int m_degreeOfParallelism;
        private bool m_enableEvaluationThrottling;
        private readonly CancellationTokenSource m_cancellationSource;

        private readonly ActionBlock<QueueItem> m_queue;

        private class QueueItem
        {
            private TaskCompletionSource<object> m_tsc;

            internal Func<Task<object>> Func { get; }

            internal Task<object> Completion => m_tsc.Task;

            internal QueueItem(Func<Task<object>> evalFunc)
            {
                Func = evalFunc;
                m_tsc = new TaskCompletionSource<object>();
            }

            internal void Execute()
            {
                try
                {
                    var result = Func().GetAwaiter().GetResult();
                    m_tsc.SetResult(result);
                }
                catch (Exception e)
                {
                    m_tsc.SetException(e);
                }
            }
        }

        /// <summary>
        /// Creates a scheduler without cancellation support (<seealso cref="EvaluationScheduler(int, bool, CancellationToken)"/>)
        /// </summary>
        public EvaluationScheduler(int degreeOfParallelism)
            : this(degreeOfParallelism, FrontEndConfigurationExtensions.DefaultEnableEvaluationThrottling, CancellationToken.None) { }

        /// <summary>
        /// Creates a scheduler.
        /// </summary>
        /// <remarks>
        /// If <paramref name="degreeOfParallelism"/> is greater than 1, then tasks provided to
        /// <see cref="EvaluateValue"/> methods are wrapped in <code>Task.Run</code>.
        /// </remarks>
        public EvaluationScheduler(int degreeOfParallelism, bool enableEvaluationThrottling, CancellationToken cancellationToken)
        {
            Contract.Requires(degreeOfParallelism >= 1);

            m_degreeOfParallelism = degreeOfParallelism;
            m_enableEvaluationThrottling = enableEvaluationThrottling;
            m_cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (m_enableEvaluationThrottling)
            {
                m_queue = new ActionBlock<QueueItem>(
                    item => item.Execute(),
                    new ExecutionDataflowBlockOptions
                    {
                        BoundedCapacity = DataflowBlockOptions.Unbounded,
                        MaxDegreeOfParallelism = m_degreeOfParallelism,
                        CancellationToken = m_cancellationSource.Token
                    }
                );
            }
        }

        /// <inheritdoc/>
        public async Task<object> EvaluateValue(Func<Task<object>> evaluateValueFunction)
        {
            if (m_degreeOfParallelism == 1)
            {
                return await evaluateValueFunction();
            }

            if (m_enableEvaluationThrottling)
            {
                var queueItem = new QueueItem(evaluateValueFunction);
                m_queue.Post(queueItem);

                // wait on either queueItem.Completion or m_queue.Completion to account for cancellation token
                // (the cancellation token only cancels m_queue.Completion)
                var finishedTask = await Task.WhenAny(queueItem.Completion, m_queue.Completion);
                return finishedTask == queueItem.Completion
                    ? await queueItem.Completion // evaluation task completed
                    : null;                      // cancelled
            }

            return m_degreeOfParallelism > 1
                ? await Task.Run(evaluateValueFunction)
                : evaluateValueFunction();
        }

        /// <inheritdoc/>
        public CancellationToken CancellationToken => m_cancellationSource.Token;

        /// <inheritdoc/>
        public void Cancel() => m_cancellationSource.Cancel();

        /// <nodoc />
        public static EvaluationScheduler Default { get; } = new EvaluationScheduler(Environment.ProcessorCount, FrontEndConfigurationExtensions.DefaultEnableEvaluationThrottling, CancellationToken.None);
    }
}
