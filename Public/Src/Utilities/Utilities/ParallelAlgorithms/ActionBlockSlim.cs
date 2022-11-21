// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

#nullable enable

namespace BuildXL.Utilities.ParallelAlgorithms
{
    /// <summary>
    /// An exception is thrown when the <see cref="ActionBlockSlim{T}"/> is full and can't accept new items.
    /// </summary>
#if DO_NOT_EXPOSE_ACTIONBLOCKSLIM // These types are used in two projects and only in one of them (BuildXL.Utilities) they should be public
internal
#else
    public
#endif // DO_NOT_EXPOSE_ACTIONBLOCKSLIM
        sealed class ActionBlockIsFullException : InvalidOperationException
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
#if DO_NOT_EXPOSE_ACTIONBLOCKSLIM // These types are used in two projects and only in one of them (BuildXL.Utilities) they should be public
internal
#else
    public
#endif // DO_NOT_EXPOSE_ACTIONBLOCKSLIM
        static class ActionBlockSlim
    {
        /// <summary>
        /// Creates an instance of the action block.
        /// </summary>
        /// <remarks>
        /// Please use this factory method only for CPU intensive (non-asynchronous) callbacks.
        /// If you need to control the concurrency for asynchronous operations, please use CreateWithAsyncAction helper.
        ///
        /// Cancellation behavior:
        /// If the given <paramref name="cancellationToken"/> is canceled, all the <see cref="ActionBlockSlim{T}.Completion"/> task is not canceled
        /// immediately. Instead, all the processing callbacks should be done first and only after that the Completion property state
        /// changes.
        /// This behavior allows to finish processing all the pending items and see their potential side effects before observing the cancellation.
        /// </remarks>
        public static ActionBlockSlim<T> Create<T>(
            int degreeOfParallelism,
            Action<T> processItemAction,
            int? capacityLimit = null,
            bool singleProducedConstrained = false,
            bool failFastOnUnhandledException = false,
            CancellationToken cancellationToken = default)
        {
            return CreateWithAsyncAction<T>(
                new ActionBlockSlimConfiguration(
                    DegreeOfParallelism: degreeOfParallelism,
                    CapacityLimit: capacityLimit,
                    SingleProducerConstrained: singleProducedConstrained,
                    FailFastOnUnhandledException: failFastOnUnhandledException
                ),
                t =>
                {
                    processItemAction(t);
                    return Task.CompletedTask;
                },
                cancellationToken);
        }

        /// <summary>
        /// Creates an instance of the action block.
        /// </summary>
        /// <remarks>
        /// Please use this factory method only for CPU intensive (non-asynchronous) callbacks.
        /// If you need to control the concurrency for asynchronous operations, please use CreateWithAsyncAction helper.
        /// </remarks>
        public static ActionBlockSlim<T> Create<T>(
            ActionBlockSlimConfiguration configuration,
            Action<T> processItemAction,
            CancellationToken cancellationToken = default)
        {
            return CreateWithAsyncAction<T>(
                configuration,
                t =>
                {
                    processItemAction(t);
                    return Task.CompletedTask;
                },
                cancellationToken);
        }

        /// <nodoc />
        public static ActionBlockSlim<T> Create<T>(
            int degreeOfParallelism,
            Func<T, Task> processItemAction,
            int? capacityLimit = null,
            bool singleProducedConstrained = false,
            bool failFastOnUnhandledException = false,
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
            Func<T, Task> processItemAction,
            int? capacityLimit = null,
            bool singleProducedConstrained = false,
            bool failFast = false,
            CancellationToken cancellationToken = default)
        {
            return CreateWithAsyncAction(
                new ActionBlockSlimConfiguration(
                    DegreeOfParallelism: degreeOfParallelism,
                    CapacityLimit: capacityLimit,
                    SingleProducerConstrained: singleProducedConstrained,
                    FailFastOnUnhandledException: failFast
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
                configuration = configuration with {DegreeOfParallelism = Environment.ProcessorCount,};
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
                configuration = configuration with {DegreeOfParallelism = Environment.ProcessorCount,};
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
    /// <param name="DegreeOfParallelism">Maximum number of processing tasks used for processing incoming data.</param>
    /// <param name="CapacityLimit">If not null then the underlying action block would have a given size and once its full <see cref="ActionBlockIsFullException"/> will be thrown.</param>
    /// <param name="SingleProducerConstrained">An optimization flag that allows the implementation to use more efficient version given the fact that only one thread will produce work items.</param>
    /// <param name="UseLongRunningTasks">If true, then the underlying tasks will be created with 'LongRunning' flag and they will use dedicated threads instead of relying on a thread pool.</param>
    /// <param name="FailFastOnUnhandledException">
    /// If true, then the task from <see cref="ActionBlockSlim{T}.Completion"/> should fail if the 'processItemAction' fails.
    /// </param>
#if DO_NOT_EXPOSE_ACTIONBLOCKSLIM // These types are used in two projects and only in one of them (BuildXL.Utilities) they should be public
    internal
#else
    public
#endif // DO_NOT_EXPOSE_ACTIONBLOCKSLIM    
        record ActionBlockSlimConfiguration(
            int DegreeOfParallelism,
            int? CapacityLimit = null,
            bool SingleProducerConstrained = false,
            bool UseLongRunningTasks = false,
            bool FailFastOnUnhandledException = false);

    /// <summary>
    /// A base class for different action-block-like implementations.
    /// </summary>
#if DO_NOT_EXPOSE_ACTIONBLOCKSLIM // These types are used in two projects and only in one of them (BuildXL.Utilities) they should be public
internal
#else
    public
#endif // DO_NOT_EXPOSE_ACTIONBLOCKSLIM
        sealed class ActionBlockSlim<T>
    {
        private readonly ActionBlockSlimConfiguration m_configuration;
        private readonly Func<T, CancellationToken, Task> m_processItemAction;
        private readonly CancellationToken m_externalCancellation;

        private readonly object m_syncRoot = new object();
        private readonly CancellationTokenSource m_internalCancellation = new CancellationTokenSource();
        private readonly List<Task> m_tasks = new List<Task>();
        private readonly Channel<T> m_channel;
        private readonly TaskCompletionSource<object?> m_tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private long m_addedWorkItems;
        private long m_processedWorkItems;
        private int m_processingWorkItems;

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
        ///
        /// </remarks>
        public int PendingWorkItems
        {
            get
            {
                Contract.Assert(m_channel.Reader.CanCount, "The channel is non countable! Did we mess up with the way we constructing it and started passing 'SingleReader' property?");
                return m_channel.Reader.Count;
            }
        }

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
                ? (ChannelOptions)new BoundedChannelOptions(m_configuration.CapacityLimit.Value) {FullMode = BoundedChannelFullMode.Wait}
                : new UnboundedChannelOptions();

            // The assumption is that the following options gives the best performance/throughput.
            options.AllowSynchronousContinuations = false;

            // Never using SingleReader option, because bounded single reader channel reader doesn't support 'Count'
            // property making our API more complicated.
            // And based on the benchmark there is no tangible performance benefits when using 'SingleReader' property.
            // The main performance difference comes from using unbounded channels that usually performs reasonably faster then unbounded
            // (but we're talking about nanoseconds here so we can use bounded or unbounded options based on the needs and not based on the slight difference in performance).
            options.SingleReader = false;
            options.SingleWriter = m_configuration.SingleProducerConstrained;

            m_channel = m_configuration.CapacityLimit != null
                ? Channel.CreateBounded<T>((BoundedChannelOptions)options)
                : Channel.CreateUnbounded<T>((UnboundedChannelOptions)options);

            // 0 concurrency is valid.
            if (m_configuration.DegreeOfParallelism != 0)
            {
                IncreaseConcurrencyTo(m_configuration.DegreeOfParallelism);
            }

            if (cancellationToken.CanBeCanceled)
            {
                // If the token supports cancellation, then we want to make sure
                // the 'Complete' state changes to 'Canceled' once the processing is done.
                var registration = cancellationToken.Register(
                    () =>
                    {
                        // Task.WhenAll returns a task which state is 'IsCanceled' if there is not faulted
                        // states and at least one of the tasks was canceled.
                        Task.WhenAll(m_tasks.ToArray()).ContinueWith(
                            t =>
                            {
                                // Checking for cancellation only because the error case is handled elsewhere.
                                if (t.IsCanceled)
                                {
                                    m_tcs.TrySetCanceled();
                                }
                            });
                    });
                
                // Removing the registration once the processing is done.
                Completion.ContinueWith(_ => { registration.Dispose(); });
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
                Contract.Assert(
                    !m_channel.Reader.Completion.IsCompleted,
                    $"Operation '{nameof(IncreaseConcurrencyTo)}' is invalid because 'Complete' method was already called.");

                var degreeOfParallelism = maxDegreeOfParallelism - DegreeOfParallelism;
                DegreeOfParallelism = maxDegreeOfParallelism;

                for (int i = 0; i < degreeOfParallelism; i++)
                {
                    var task = CreateProcessorItemTask();

                    if (m_configuration.FailFastOnUnhandledException)
                    {
                        task.ContinueWith(
                            t =>
                            {
                                if (t.IsFaulted)
                                {
                                    // Unwrapping an exception if possible.
                                    m_tcs.TrySetException(t.Exception?.InnerExceptions.Count == 1 ? t.Exception.InnerExceptions[0] : t.Exception!);
                                }
                            });
                    }

                    m_tasks.Add(task);
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

                    // Not handling the cancellation here because we want the resulting task to be canceled
                    while (await m_channel.Reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
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
        public async ValueTask<bool> PostAsync(T item, CancellationToken cancellationToken = default)
        {
            if (m_internalCancellation.IsCancellationRequested)
            {
                return false;
            }

            await m_channel.Writer.WriteAsync(item, cancellationToken);
            Interlocked.Increment(ref m_addedWorkItems);
            return true;
        }

        /// <summary>
        /// Add a given <paramref name="item"/> to a processing queue. This method will is non-blocking and will throw.
        /// </summary>
        /// <exception cref="ActionBlockIsFullException">If the queue is full or complete</exception>
        /// <remarks>
        /// <paramref name="throwOnFullOrComplete"/> is false by default to mimic the behavior of the TPL-based ActionBlock that was not failing on 'Post'.
        /// </remarks>
        public void Post(T item, bool throwOnFullOrComplete = false)
        {
            TryPost(item, throwOnFullOrComplete);
        }

        /// <summary>
        /// Post all the given items and returns the completion task.
        /// </summary>
        public Task PostAllAndComplete(IEnumerable<T> items)
        {
            // Mimic the existing behavior
            foreach (var item in items)
            {
                bool added = TryPost(item);
                Contract.Assert(added);
            }

            Complete();
            return Completion;
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
                    $"Couldn't add new item. Pending=[{currentCount}] Capacity=[{m_configuration.CapacityLimit}] Completed=[{m_channel.Reader.Completion.IsCompleted}]",
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
                    // Cancelling pending operations won't change the state of 'Completion' property to 'Canceled'
                    // this will happen, only when the external token is canceled.
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
                                m_tcs.TrySetException(exception!);
                            }
                            else
                            {
                                m_tcs.TrySetResult(null);
                            }
                        });
                }
            }
        }
    }
}