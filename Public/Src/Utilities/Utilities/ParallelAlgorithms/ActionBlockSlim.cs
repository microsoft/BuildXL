// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
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

    /// <nodoc />
    public static class ActionBlockSlim
    {
        /// <summary>
        /// Set it to true to globally use <see cref="ActionBlockSlim{T}.ChannelBasedActionBlockSlim"/> by default.
        /// </summary>
        public static bool UseChannelBaseImplementationByDefault = false;

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
            bool? useChannelBasedImpl = null, 
            bool? singleProducedConstrained = null, 
            bool? singleConsumerConstrained = null, 
            CancellationToken cancellationToken = default)
        {
            degreeOfParallelism = degreeOfParallelism == -1 ? Environment.ProcessorCount : degreeOfParallelism;

            return (useChannelBasedImpl ?? UseChannelBaseImplementationByDefault)
                ? (ActionBlockSlim<T>)new ActionBlockSlim<T>.ChannelBasedActionBlockSlim(degreeOfParallelism, processItemAction,
                    capacityLimit, singleProducedConstrained, singleConsumerConstrained, cancellationToken)
                : new ActionBlockSlim<T>.SemaphoreBasedActionBlockSlim(degreeOfParallelism, processItemAction, capacityLimit);
        }

        /// <nodoc />
        public static ActionBlockSlim<T> CreateWithAsyncAction<T>(
            int degreeOfParallelism, 
            Func<T, Task> processItemAction,
            int? capacityLimit = null, 
            bool? useChannelBasedImpl = null, 
            bool? singleProducedConstrained = null, 
            bool? singleConsumerConstrained = null,
            CancellationToken cancellationToken = default)
        {
            degreeOfParallelism = degreeOfParallelism == -1 ? Environment.ProcessorCount : degreeOfParallelism;

            return (useChannelBasedImpl ?? UseChannelBaseImplementationByDefault)
                ? (ActionBlockSlim<T>)new ActionBlockSlim<T>.ChannelBasedActionBlockSlim(degreeOfParallelism, processItemAction,
                    capacityLimit, singleProducedConstrained, singleConsumerConstrained, cancellationToken)
                : new ActionBlockSlim<T>.SemaphoreBasedActionBlockSlim(degreeOfParallelism, processItemAction, capacityLimit);
        }
    }

    /// <summary>
    /// A base class for different action-block-like implementations.
    /// </summary>
    public abstract class ActionBlockSlim<T>
    {
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
        /// Add a given <paramref name="item"/> to a processing queue.
        /// </summary>
        /// <exception cref="ActionBlockIsFullException">If the queue is full and the queue was configured to limit the queue size.</exception>
        public abstract void Post(T item);

        /// <summary>
        /// Marks the action block as completed.
        /// </summary>
        public virtual void Complete()
        {
            if (Interlocked.CompareExchange(ref m_schedulingCompleted, value: 1, comparand: 0) == 0)
            {
                CompleteCore();
            }
        }

        /// <nodoc />
        protected abstract void CompleteCore();

        /// <nodoc />
        protected bool SchedulingCompleted() => Volatile.Read(ref m_schedulingCompleted) != 0;

        /// <summary>
        /// Creates a task responsible for draining the action block queue.
        /// </summary>
        protected abstract Task CreateProcessorItemTask(int degreeOfParallelism);

        /// <summary>
        /// Fails if the block is completed.
        /// </summary>
        protected void AssertNotCompleted([CallerMemberName] string callerName = null)
        {
            if (SchedulingCompleted())
            {
                Contract.Assert(false, $"Operation '{callerName}' is invalid because 'Complete' method was already called.");
            }
        }

        /// <summary>
        /// Returns a task that will be completed when <see cref="Complete"/> method is called and all the items added to the queue are processed.
        /// </summary>
        public virtual Task CompletionAsync()
        {
            // Awaiting all the tasks to be finished.
            return Task.WhenAll(Tasks.ToArray());
        }

        /// <summary>
        /// Increases the current concurrency level from <see cref="DegreeOfParallelism"/> to <paramref name="maxDegreeOfParallelism"/>.
        /// </summary>
        public virtual void IncreaseConcurrencyTo(int maxDegreeOfParallelism)
        {
            Contract.Requires(maxDegreeOfParallelism > DegreeOfParallelism);
            AssertNotCompleted();

            var degreeOfParallelism = maxDegreeOfParallelism - DegreeOfParallelism;
            DegreeOfParallelism = maxDegreeOfParallelism;

            for (int i = 0; i < degreeOfParallelism; i++)
            {
                Tasks.Add(CreateProcessorItemTask(degreeOfParallelism));
            }
        }

        /// <summary>
        /// Light-weight version of a non-dataflow block that invokes a provided <see cref="Action{T}"/> delegate for every data element received in parallel with limited concurrency.
        /// </summary>
        internal sealed class SemaphoreBasedActionBlockSlim : ActionBlockSlim<T>
        {
            private readonly ConcurrentQueue<T> m_queue;

            private readonly SemaphoreSlim m_semaphore;

            /// <nodoc />
            internal SemaphoreBasedActionBlockSlim(int degreeOfParallelism, Action<T> processItemAction,
                int? capacityLimit = null)
                : this(degreeOfParallelism, t =>
                {
                    processItemAction(t);
                    return Task.CompletedTask;
                }, capacityLimit: capacityLimit)
            {
            }

            /// <nodoc />
            internal SemaphoreBasedActionBlockSlim(int degreeOfParallelism, Func<T, Task> processItemAction,
                int? capacityLimit = null)
            {
                Contract.Requires(degreeOfParallelism >= -1);

                ProcessItemAction = processItemAction;
                CapacityLimit = capacityLimit;

                m_queue = new ConcurrentQueue<T>();

                // Semaphore count is 0 to ensure that all the tasks are blocked unless new data is scheduled.
                m_semaphore = new SemaphoreSlim(0, int.MaxValue);

                // 0 concurrency is valid.
                if (degreeOfParallelism != 0)
                {
                    IncreaseConcurrencyTo(degreeOfParallelism);
                }
            }

            /// <inheritdoc />
            public override void Post(T item)
            {
                AssertNotCompleted();

                var currentCount = Interlocked.Increment(ref Pending);
                if (CapacityLimit != null && currentCount > CapacityLimit.Value)
                {
                    Interlocked.Decrement(ref Pending);

                    throw new ActionBlockIsFullException(
                        $"Can't add new item because the queue is full. Capacity is '{CapacityLimit.Value}'. CurrentCount is '{currentCount}'.",
                        CapacityLimit.Value, currentCount);
                }

                // NOTE: Enqueue MUST happen before releasing the semaphore
                // to ensure WaitAsync below never returns when there is not
                // a corresponding item in the queue to be dequeued. The only
                // exception is on completion of all items.
                m_queue.Enqueue(item);
                m_semaphore.Release();
            }

            /// <inheritdoc />
            protected override void CompleteCore()
            {
                // Release one thread that will release all the threads when all the elements are processed.
                m_semaphore.Release();
            }

            /// <inheritdoc />
            protected override Task CreateProcessorItemTask(int degreeOfParallelism)
            {
                return Task.Run(
                    async () =>
                    {
                        while (true)
                        {
                            await m_semaphore.WaitAsync();

                            if (m_queue.TryDequeue(out var item))
                            {
                                await ProcessItemAction(item);
                            }

                            // Could be -1 if the number of pending items is already 0 and the task was awakened for graceful finish.
                            if (Interlocked.Decrement(ref Pending) <= 0 && SchedulingCompleted())
                            {
                                // Ensure all tasks are unblocked and can gracefully
                                // finish since there are at most degreeOfParallelism - 1 tasks
                                // waiting at this point
                                m_semaphore.Release(degreeOfParallelism);
                                return;
                            }
                        }
                    });
            }
        }

        /// <summary>
        /// An action-block implementation based on System.Threading.Channel.
        /// </summary>
        internal sealed class ChannelBasedActionBlockSlim : ActionBlockSlim<T>
        {
            private readonly Channel<T> m_channel;
            private readonly CancellationToken m_cancellationToken;

            /// <nodoc />
            internal ChannelBasedActionBlockSlim(int degreeOfParallelism, Action<T> processItemAction,
                int? capacityLimit = null,
                bool? singleProducedConstrained = null, 
                bool? singleConsumerConstrained = null,
                CancellationToken cancellationToken = default)
                : this (degreeOfParallelism, t =>
                {
                    processItemAction(t);
                    return Task.CompletedTask;
                }, capacityLimit: capacityLimit, singleProducedConstrained, singleConsumerConstrained, cancellationToken)
            {
            }

            /// <nodoc />
            internal ChannelBasedActionBlockSlim(int degreeOfParallelism, Func<T, Task> processItemAction,
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

            /// <inheritdoc />
            public override void Post(T item)
            {
                AssertNotCompleted();

                if (!m_channel.Writer.TryWrite(item))
                {
                    // There are two cases when this may happen: the channel is completed or
                    // we have a bounded channel and its full.

                    AssertNotCompleted();

                    Contract.Assert(CapacityLimit != null);
                    throw new ActionBlockIsFullException(
                        $"Can't add new item because the queue is full. Capacity is '{CapacityLimit.Value}'. CurrentCount is '{Pending}'.",
                        CapacityLimit.Value, Pending);
                }

                // Technically, we have a race condition here: we can decrement the counter in the processing block before this one is incremented.
                Interlocked.Increment(ref Pending);
            }

            /// <inheritdoc />
            protected override void CompleteCore()
            {
                m_channel.Writer.Complete();
            }

            /// <inheritdoc />
            public override Task CompletionAsync()
            {
                // The number of processing task could be changed via IncreaseConcurrencyTo method calls,
                // so we need to make sure that Complete method was called by "awaiting" for the task completion source.

                return Task.WhenAll(Tasks.ToArray());
            }

            /// <inheritdoc />
            protected override Task CreateProcessorItemTask(int degreeOfParallelism)
            {
                return Task.Run(
                    async () =>
                    {
                        // Not using 'Reader.ReadAllAsync' because its not available in the version we use here.
                        // So we do what 'ReadAllAsync' does under the hood.
                        while (await m_channel.Reader.WaitToReadAsync(m_cancellationToken).ConfigureAwait(false))
                        {
                            while (m_channel.Reader.TryRead(out var item))
                            {
                                await ProcessItemAction(item);
                                Interlocked.Decrement(ref Pending);
                            }
                        }
                    });
            }
        }
    }
}
