// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BuildXL.Utilities.ParallelAlgorithms
{
    /// <summary>
    /// An exception is thrown when the <see cref="ActionBlockSlim{T}"/> is full and can't accept new items.
    /// </summary>
    public sealed class ActionBlockIsFullException : InvalidOperationException
    {
        /// <nodoc />
        public int ConcurrencyLimit { get; }

        /// <nodoc />
        public int CurrentCount { get; }

        /// <nodoc />
        public ActionBlockIsFullException(string message, int concurrencyLimit, int currentCount)
            : base(message)
        {
            ConcurrencyLimit = concurrencyLimit;
            CurrentCount = currentCount;
        }
    }

    /// <summary>
    /// A non-static factory for creating <see cref="ActionBlockSlim{T}"/> instances.
    /// </summary>
    public static class ActionBlockSlim
    {
        /// <summary>
        /// Creates an instance of the action block.
        /// </summary>
        /// <remarks>
        /// Please use this factory method only for CPU intensive (non-asynchronous) callbacks.
        /// If you need to control the concurrency for asynchronous operations, please use <see cref="CreateWithAsyncAction{T}"/> helper.
        /// </remarks>
        public static ActionBlockSlim<T> Create<T>(
            int degreeOfParallelism,
            Action<T> processItemAction,
            int? capacityLimit = null,
            bool? singleProducedConstrained = null,
            bool? singleConsumerConstrained = null,
            CancellationToken cancellationToken = default)
        {
            degreeOfParallelism = degreeOfParallelism == -1 ? Environment.ProcessorCount : degreeOfParallelism;

            return new ActionBlockSlim<T>(
                degreeOfParallelism,
                processItemAction,
                capacityLimit,
                singleProducedConstrained,
                singleConsumerConstrained,
                cancellationToken);
        }

        /// <nodoc />
        public static ActionBlockSlim<T> CreateWithAsyncAction<T>(
            int degreeOfParallelism,
            Func<T, Task> processItemAction,
            int? capacityLimit = null,
            bool? singleProducedConstrained = null,
            bool? singleConsumerConstrained = null,
            CancellationToken cancellationToken = default)
        {
            degreeOfParallelism = degreeOfParallelism == -1 ? Environment.ProcessorCount : degreeOfParallelism;

            return new ActionBlockSlim<T>(
                degreeOfParallelism,
                processItemAction,
                capacityLimit,
                singleProducedConstrained,
                singleConsumerConstrained,
                cancellationToken);
        }
    }

    /// <summary>
    /// A base class for different action-block-like implementations.
    /// </summary>
    public class ActionBlockSlim<T>
    {
        private readonly Channel<T> m_channel;
        private readonly CancellationToken m_cancellationToken;

        private int m_schedulingCompleted = 0;

        /// <nodoc />
        protected Func<T, Task> ProcessItemAction;
        /// <nodoc />
        protected int? CapacityLimit;
        /// <nodoc />
        protected List<Task> Tasks = new List<Task>();
        /// <nodoc />
        protected int Pending;

        /// <summary>
        /// Returns the number of pending items.
        /// </summary>
        public int PendingWorkItems => Pending;

        /// <summary>
        /// Current degree of parallelism.
        /// </summary>
        public int DegreeOfParallelism { get; protected set; }

        /// <summary>
        /// Gets whether the action block is complete
        /// </summary>
        public bool IsComplete => m_schedulingCompleted == 1 && Tasks.All(t => t.IsCompleted);

        /// <summary>
        /// Used to cancel all pending operations
        /// </summary>
        protected CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        /// <nodoc />
        internal ActionBlockSlim(int degreeOfParallelism, Action<T> processItemAction,
            int? capacityLimit = null,
            bool? singleProducedConstrained = null,
            bool? singleConsumerConstrained = null,
            CancellationToken cancellationToken = default)
            : this(degreeOfParallelism, t =>
            {
                processItemAction(t);
                return Task.CompletedTask;
            }, capacityLimit: capacityLimit, singleProducedConstrained, singleConsumerConstrained, cancellationToken)
        {
        }

        /// <nodoc />
        internal ActionBlockSlim(int degreeOfParallelism, Func<T, Task> processItemAction,
            int? capacityLimit = null,
            bool? singleProducedConstrained = null,
            bool? singleConsumerConstrained = null,
            CancellationToken cancellationToken = default)
        {
            ProcessItemAction = processItemAction;
            CapacityLimit = capacityLimit;

            m_cancellationToken = cancellationToken;

            var options = capacityLimit != null
                // Blocking the calls if the channel is full to handle 
                ? (ChannelOptions)new BoundedChannelOptions(capacityLimit.Value)
                { FullMode = BoundedChannelFullMode.Wait }
                : new UnboundedChannelOptions();

            // The assumption is that the following options gives the best performance/throughput.
            options.AllowSynchronousContinuations = false;
            options.SingleReader = singleConsumerConstrained ?? false;
            options.SingleWriter = singleProducedConstrained ?? false;

            m_channel = capacityLimit != null
                ? Channel.CreateBounded<T>((BoundedChannelOptions)options)
                : Channel.CreateUnbounded<T>((UnboundedChannelOptions)options);

            // 0 concurrency is valid.
            if (degreeOfParallelism != 0)
            {
                IncreaseConcurrencyTo(degreeOfParallelism);
            }
        }

        /// <summary>
        /// Add a given <paramref name="item"/> to a processing queue.
        /// </summary>
        /// <exception cref="ActionBlockIsFullException">If the queue is full and the queue was configured to limit the queue size.</exception>
        public void Post(T item)
        {
            TryPost(item, throwOnFullOrComplete: true);
        }

        /// <summary>
        /// Add a given <paramref name="item"/> to a processing queue.
        /// </summary>
        /// <exception cref="ActionBlockIsFullException">If the queue is full and the queue was configured to limit the queue size.</exception>
        public bool TryPost(T item, bool throwOnFullOrComplete = false)
        {
            if (!TryIncrementPending(throwOnFullOrComplete))
            {
                return false;
            }

            bool added = m_channel.Writer.TryWrite(item);
            Contract.Assert(added);
            return true;
        }

        private bool TryIncrementPending(bool throwOnFullOrComplete)
        {
            if (!AssertNotCompleted(shouldThrow: throwOnFullOrComplete))
            {
                return false;
            }

            var currentCount = Interlocked.Increment(ref Pending);
            if (CapacityLimit != null && currentCount > CapacityLimit.Value)
            {
                Interlocked.Decrement(ref Pending);

                if (throwOnFullOrComplete)
                {
                    throw new ActionBlockIsFullException(
                        $"Can't add new item because the queue is full. Capacity is '{CapacityLimit.Value}'. CurrentCount is '{currentCount}'.",
                        CapacityLimit.Value, currentCount);
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Marks the action block as completed.
        /// </summary>
        public void Complete(bool cancelPending = false)
        {
            if (cancelPending)
            {
                Cancellation.Cancel();
            }

            if (Interlocked.CompareExchange(ref m_schedulingCompleted, value: 1, comparand: 0) == 0)
            {
                m_channel.Writer.Complete();
            }
        }

        /// <nodoc />
        private bool SchedulingCompleted() => Volatile.Read(ref m_schedulingCompleted) != 0;

        private Task CreateProcessorItemTask()
        {
            return Task.Run(
                async () =>
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(Cancellation.Token, m_cancellationToken);

                    // Not using 'Reader.ReadAllAsync' because its not available in the version we use here.
                    // So we do what 'ReadAllAsync' does under the hood.
                    //
                    // using 'WaitToReadOrCanceledAsync' instead of 'channel.Reader.WaitToReadAsync' to simply break
                    // the execution when the token is triggered instead of throwing 'OperationCanceledException'
                    while (await m_channel.WaitToReadOrCanceledAsync(cts.Token).ConfigureAwait(false))
                    {
                        while (!cts.Token.IsCancellationRequested && m_channel.Reader.TryRead(out var item))
                        {
                            await ProcessItemAction(item);
                            Interlocked.Decrement(ref Pending);
                        }
                    }
                });
        }

        /// <summary>
        /// Fails if the block is completed.
        /// </summary>
        protected bool AssertNotCompleted(bool shouldThrow = true, [CallerMemberName] string callerName = null)
        {
            if (SchedulingCompleted())
            {
                if (shouldThrow)
                {
                    Contract.Assert(false, $"Operation '{callerName}' is invalid because 'Complete' method was already called.");
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns a task that will be completed when <see cref="Complete"/> method is called and all the items added to the queue are processed.
        /// </summary>
        public virtual Task CompletionAsync()
        {
            // The number of processing task could be changed via IncreaseConcurrencyTo method calls,
            // so we need to make sure that Complete method was called by "awaiting" for the task completion source.

            return Task.WhenAll(Tasks.ToArray());
        }

        /// <summary>
        /// Increases the current concurrency level from <see cref="DegreeOfParallelism"/> to <paramref name="maxDegreeOfParallelism"/>.
        /// </summary>
        public void IncreaseConcurrencyTo(int maxDegreeOfParallelism)
        {
            Contract.Requires(maxDegreeOfParallelism > DegreeOfParallelism);
            AssertNotCompleted();

            var degreeOfParallelism = maxDegreeOfParallelism - DegreeOfParallelism;
            DegreeOfParallelism = maxDegreeOfParallelism;

            for (int i = 0; i < degreeOfParallelism; i++)
            {
                Tasks.Add(CreateProcessorItemTask());
            }
        }
    }
}
