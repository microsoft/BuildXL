// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;

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
        /// If you need to control the concurrency for asynchronous operations, please use CreateWithAsyncAction helper.
        /// </remarks>
        public static ActionBlockSlim<T> Create<T>(
            int degreeOfParallelism,
            Action<T> processItemAction,
            int? capacityLimit = null,
            bool singleProducedConstrained = false,
            CancellationToken cancellationToken = default)
        {
            return CreateWithAsyncAction<T>(
                new ActionBlockSlimConfiguration(
                    DegreeOfParallelism: degreeOfParallelism,
                    CapacityLimit: capacityLimit,
                    SingleProducerConstrained: singleProducedConstrained
                ),
                t =>
                {
                    processItemAction(t);
                    return Task.CompletedTask;
                },
                cancellationToken);
        }

        /// <nodoc />
        public static ActionBlockSlim<T> CreateWithAsyncAction<T>(
            int degreeOfParallelism,
            Func<T, Task> processItemAction,
            int? capacityLimit = null,
            bool singleProducedConstrained = false,
            CancellationToken cancellationToken = default)
        {
            return CreateWithAsyncAction(
                new ActionBlockSlimConfiguration(
                    DegreeOfParallelism: degreeOfParallelism,
                    CapacityLimit: capacityLimit,
                    SingleProducerConstrained: singleProducedConstrained
                ),
                processItemAction,
                cancellationToken);
        }
        
        /// <nodoc />
        public static ActionBlockSlim<T> CreateWithAsyncAction<T>(
            int degreeOfParallelism,
            Func<T, CancellationToken, Task> processItemAction,
            int? capacityLimit = null,
            bool singleProducedConstrained = false,
            CancellationToken cancellationToken = default)
        {
            return CreateWithAsyncAction(
                new ActionBlockSlimConfiguration(
                    DegreeOfParallelism: degreeOfParallelism,
                    CapacityLimit: capacityLimit,
                    SingleProducerConstrained: singleProducedConstrained
                ),
                processItemAction,
                cancellationToken);
        }

        /// <nodoc />
        public static ActionBlockSlim<T> CreateWithAsyncAction<T>(
            ActionBlockSlimConfiguration configuration,
            Func<T, Task> processItemAction,
            CancellationToken cancellationToken = default)
        {
            if (configuration.DegreeOfParallelism == -1)
            {
                configuration = configuration with
                {
                    DegreeOfParallelism = Environment.ProcessorCount,
                };
            }

            return new ActionBlockSlim<T>(
                configuration,
                (t, token) => processItemAction(t),
                cancellationToken);
        }
        
        /// <nodoc />
        public static ActionBlockSlim<T> CreateWithAsyncAction<T>(
            ActionBlockSlimConfiguration configuration,
            Func<T, CancellationToken, Task> processItemAction,
            CancellationToken cancellationToken = default)
        {
            if (configuration.DegreeOfParallelism == -1)
            {
                configuration = configuration with
                {
                    DegreeOfParallelism = Environment.ProcessorCount,
                };
            }

            return new ActionBlockSlim<T>(
                configuration,
                processItemAction,
                cancellationToken);
        }
    }

    /// <summary>
    /// Configuration object for <see cref="ActionBlockSlim{T}"/>
    /// </summary>
    public record ActionBlockSlimConfiguration(
        int DegreeOfParallelism,
        int? CapacityLimit = null,
        bool SingleProducerConstrained = false,
        bool UseLongRunningTasks = false);

    /// <summary>
    /// A base class for different action-block-like implementations.
    /// </summary>
    public sealed class ActionBlockSlim<T>
    {
        private readonly ActionBlockSlimConfiguration m_configuration;
        private readonly Func<T, CancellationToken, Task> m_processItemAction;
        private readonly CancellationToken m_externalCancellation;

        private readonly object m_syncRoot = new object();
        private readonly CancellationTokenSource m_internalCancellation = new CancellationTokenSource();
        private readonly List<Task> m_tasks = new List<Task>();
        private readonly Channel<T> m_channel;
        private readonly TaskSourceSlim<Unit> m_tcs = TaskSourceSlim.Create<Unit>();

        private long m_addedWorkItems = 0;

        private long m_processedWorkItems = 0;

        private int m_processingWorkItems = 0;

        /// <summary>
        /// Current degree of parallelism.
        /// </summary>
        public int DegreeOfParallelism { get; private set; }

        /// <summary>
        /// Returns a task that will be completed when <see cref="Complete"/> method is called and all the items added to the queue are processed.
        /// </summary>
        public Task Completion => m_tcs.Task;

        /// <summary>
        /// Returns the number of pending items. Should only be used for tests or telemetry.
        /// </summary>
        /// <remarks>
        /// The number reported here is the number of items in the queue, and does not account for items currently 
        /// being processed.
        /// 
        /// Please note, adding <see cref="PendingWorkItems"/> and <see cref="ProcessingWorkItems"/> may not yield an
        /// accurate estimate of the number of work items in the action block because there is no thread-safe way to do
        /// so with the way it is currently written.
        /// </remarks>
        public int PendingWorkItems => m_channel.Reader.Count;

        /// <summary>
        /// Returns the number of items being concurrently processed. Should only be used for tests or telemetry.
        /// </summary>
        /// <remarks>
        /// This does not account for the number of items currently in the queue.
        /// 
        /// Please note, adding <see cref="PendingWorkItems"/> and <see cref="ProcessingWorkItems"/> may not yield an
        /// accurate estimate of the number of work items in the action block because there is no thread-safe way to do
        /// so with the way it is currently written.
        /// </remarks>
        public int ProcessingWorkItems => m_processingWorkItems;

        /// <nodoc />
        public long AddedWorkItems => m_addedWorkItems;

        /// <nodoc />
        public long ProcessedWorkItems => m_processedWorkItems;

        /// <nodoc />
        internal ActionBlockSlim(
            ActionBlockSlimConfiguration configuration, 
            Func<T, CancellationToken, Task> processItemAction,
            CancellationToken cancellationToken = default)
        {
            m_configuration = configuration;
            m_processItemAction = processItemAction;
            m_externalCancellation = cancellationToken;

            var options = m_configuration.CapacityLimit != null
                // Blocking the calls if the channel is full to handle 
                ? (ChannelOptions)new BoundedChannelOptions(m_configuration.CapacityLimit.Value)
                { FullMode = BoundedChannelFullMode.Wait }
                : new UnboundedChannelOptions();

            // The assumption is that the following options gives the best performance/throughput.
            options.AllowSynchronousContinuations = false;
            options.SingleReader = m_configuration.DegreeOfParallelism == 1;
            options.SingleWriter = m_configuration.SingleProducerConstrained;

            m_channel = m_configuration.CapacityLimit != null
                ? Channel.CreateBounded<T>((BoundedChannelOptions)options)
                : Channel.CreateUnbounded<T>((UnboundedChannelOptions)options);

            // 0 concurrency is valid.
            if (m_configuration.DegreeOfParallelism != 0)
            {
                IncreaseConcurrencyTo(m_configuration.DegreeOfParallelism);
            }
        }

        /// <summary>
        /// Increases the current concurrency level from <see cref="DegreeOfParallelism"/> to <paramref name="maxDegreeOfParallelism"/>.
        /// </summary>
        public void IncreaseConcurrencyTo(int maxDegreeOfParallelism)
        {
            Contract.Requires(maxDegreeOfParallelism > DegreeOfParallelism);

            lock (m_syncRoot)
            {
                Contract.Assert(!m_channel.Reader.Completion.IsCompleted, $"Operation '{nameof(IncreaseConcurrencyTo)}' is invalid because 'Complete' method was already called.");

                var degreeOfParallelism = maxDegreeOfParallelism - DegreeOfParallelism;
                DegreeOfParallelism = maxDegreeOfParallelism;

                for (int i = 0; i < degreeOfParallelism; i++)
                {
                    m_tasks.Add(CreateProcessorItemTask());
                }
            }
        }

        private Task CreateProcessorItemTask()
        {
            var taskCreationOptions = TaskCreationOptions.None;
            if (m_configuration.UseLongRunningTasks)
            {
                taskCreationOptions = TaskCreationOptions.LongRunning;
            }

            var taskCreationTask = Task.Factory.StartNew(
                async () =>
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(m_internalCancellation.Token, m_externalCancellation);

                    // Not using 'Reader.ReadAllAsync' because its not available in the version we use here.
                    // So we do what 'ReadAllAsync' does under the hood.
                    //
                    // using 'WaitToReadOrCanceledAsync' instead of 'channel.Reader.WaitToReadAsync' to simply break
                    // the execution when the token is triggered instead of throwing 'OperationCanceledException'
                    while (await m_channel.WaitToReadOrCanceledAsync(cts.Token).ConfigureAwait(false))
                    {
                        while (!cts.Token.IsCancellationRequested && m_channel.Reader.TryRead(out var item))
                        {
                            Interlocked.Increment(ref m_processingWorkItems);
                            try
                            {
                                await m_processItemAction(item, cts.Token);
                            }
                            finally
                            {
                                Interlocked.Decrement(ref m_processingWorkItems);
                                Interlocked.Increment(ref m_processedWorkItems);
                            }
                        }
                    }
                },
                taskCreationOptions);

            return taskCreationTask.Unwrap();
        }

        /// <summary>
        /// Add a given <paramref name="item"/> to a processing queue. Awaiting on this task will block until we the
        /// item has been added to the processing queue, which can take some time if the processing of items is slower
        /// than the rate at which elements are added to the queue.
        /// </summary>
        public async ValueTask PostAsync(T item, CancellationToken cancellationToken = default)
        {
            await m_channel.Writer.WriteAsync(item, cancellationToken);
            Interlocked.Increment(ref m_addedWorkItems);
        }

        /// <summary>
        /// Add a given <paramref name="item"/> to a processing queue. This method will is non-blocking and will throw.
        /// </summary>
        /// <exception cref="ActionBlockIsFullException">If the queue is full or complete</exception>
        public void Post(T item, bool throwOnFullOrComplete = true)
        {
            TryPost(item, throwOnFullOrComplete);
        }

        /// <summary>
        /// Add a given <paramref name="item"/> to a processing queue. This method is non-blocking and will optionally
        /// throw.
        /// </summary>
        /// <exception cref="ActionBlockIsFullException">If the queue is full or complete</exception>
        public bool TryPost(T item, bool throwOnFullOrComplete = false)
        {
            var added = m_channel.Writer.TryWrite(item);
            if (!added && throwOnFullOrComplete)
            {
                var currentCount = PendingWorkItems;
                // Please be aware: currentCount in the following exception is read after the actual failure to write,
                // so it is quite likely that you will get a currentCount that is less than the configured capacity.
                throw new ActionBlockIsFullException(
                    $"Couldn't add new item. Pending=[{currentCount}] Capacity=[{m_configuration.CapacityLimit.Value}] Completed=[{m_channel.Reader.Completion.IsCompleted}]",
                    concurrencyLimit: m_configuration.DegreeOfParallelism,
                    currentCount: currentCount);
            }

            if (added)
            {
                Interlocked.Increment(ref m_addedWorkItems);
            }

            return added;
        }

        /// <summary>
        /// Marks the action block as completed.
        /// </summary>
        public void Complete(bool cancelPending = false, bool propagateExceptionsFromCallback = true)
        {
            lock (m_syncRoot)
            {
                if (cancelPending)
                {
                    m_internalCancellation.Cancel();
                }

                if (!m_channel.Reader.Completion.IsCompleted)
                {
                    m_channel.Writer.Complete();

                    Task.WhenAll(m_tasks.ToArray()).ContinueWith(
                        t =>
                        {
                            // Not handling the cancellation state because its not possible.

                            // It is very important to check t.Exception property first to avoid
                            // task unobserved errors when propagateExceptionsFromCallback is false.
                            if (t.Exception is not null && propagateExceptionsFromCallback)
                            {
                                // If the AggregateException has a single one, then passing it to the target tcs
                                // to avoid adding layer of AggregateExceptions.
                                var exception = t.Exception.InnerExceptions.Count == 1 ? t.Exception.InnerException : t.Exception;
                                m_tcs.TrySetException(exception);
                            }
                            else
                            {
                                m_tcs.TrySetResult(Unit.Void);
                            }
                        });
                }
            }
        }
    }
}
