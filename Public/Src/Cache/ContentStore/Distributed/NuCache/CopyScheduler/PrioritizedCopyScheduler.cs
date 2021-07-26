// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    /// <summary>
    /// Schedules copies in a prioritized fashion. Copies will enter a queue for their priority class, and will be
    /// dispatched in the order in which they came. Higher priority copies are expected to happen earlier than lower
    /// priority ones in the vast majority of cases.
    ///
    /// The main function that enqueues a copy is <see cref="EnqueueAsync{T}(CopyOperationBase)"/>, while the dispatch
    /// happens inside <see cref="Scheduler(OperationContext)"/>.
    /// </summary>
    public sealed class PrioritizedCopyScheduler : StartupShutdownSlimBase, ICopyScheduler
    {
        private record CopyTask
        {
            public TaskSourceSlim<object> TaskSource { get; } = TaskSourceSlim.Create<object>();

            public CopyOperationBase Request { get; }

            public int Priority { get; }

            public StopwatchSlim Stopwatch { get; }

            public CancellationToken TimeoutToken { get; }

            public CancellationToken CancelToken { get; }

            public int Dequeued = 0;

            public CopyTask(CopyOperationBase request, int priority, CancellationToken timeoutToken, CancellationToken cancelToken)
            {
                Request = request;
                Priority = priority;
                Stopwatch = StopwatchSlim.Start();
                TimeoutToken = timeoutToken;
                CancelToken = cancelToken;
            }
        }

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(PrioritizedCopyScheduler));

        private readonly PrioritizedCopySchedulerConfiguration _configuration;

        private readonly IProducerConsumerCollection<CopyTask>[] _pending;

        private int _numPending;
        private readonly int[] _numPendingByPriority;

        private int _numInflight;

        private readonly ICopySchedulerPriorityAssigner _assigner;

        // Caching the metric names to avoid excessive allocations during metric names construction.
        private readonly string[] _createdMetricNames;
        private readonly string[] _throttledMetricNames;
        private readonly string[] _failedAddMetricNames;
        private readonly string[] _timeoutMetricNames;
        private readonly string[] _enqueuedMetricNames;
        private readonly string[] _executedMetricNames;

        private Task? _schedulerTask;

        // NOTE: we don't dispose of these fields because copies may take some amount of time to cancel, so if we did
        // that we could end up throwing ObjectDisposedException on copies when shutting down.
        private readonly EventWaitHandle _copyCompletedEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset);
        private readonly EventWaitHandle _copyAddedEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset);

        /// <nodoc />
        public PrioritizedCopyScheduler(PrioritizedCopySchedulerConfiguration configuration)
        {
            Contract.Requires(configuration.MaximumConcurrentCopies > 0);
            Contract.Requires(configuration.ReservedCapacityPerCycleRate >= 0 && configuration.ReservedCapacityPerCycleRate < 1);

            _configuration = configuration;

            switch (configuration.PriorityAssignmentStrategy)
            {
                case PrioritizedCopySchedulerPriorityAssignmentStrategy.Default:
                    _assigner = new DefaultPriorityAssigner();
                    break;
                default:
                    throw new NotImplementedException($"Unsupported {nameof(PrioritizedCopySchedulerPriorityAssignmentStrategy)} value {configuration.PriorityAssignmentStrategy}");
            }

            _numPendingByPriority = new int[_assigner.MaxPriority + 1];

            switch (configuration.QueuePopOrder)
            {
                case SemaphoreOrder.FIFO:
                    _pending = Enumerable.Range(0, _assigner.MaxPriority + 1).Select(p => new ConcurrentQueue<CopyTask>()).ToArray();
                    break;
                case SemaphoreOrder.LIFO:
                    _pending = Enumerable.Range(0, _assigner.MaxPriority + 1).Select(p => new ConcurrentStack<CopyTask>()).ToArray();
                    break;
                default:
                    throw new NotImplementedException($"Unsupported {nameof(SemaphoreOrder)} value {configuration.QueuePopOrder}");
            }

            _createdMetricNames = Enumerable.Range(0, _assigner.MaxPriority + 1).Select(n => $"CopyScheduler_Created_P{n}").ToArray();
            _throttledMetricNames = Enumerable.Range(0, _assigner.MaxPriority + 1).Select(n => $"CopyScheduler_Throttled_P{n}").ToArray();
            _failedAddMetricNames = Enumerable.Range(0, _assigner.MaxPriority + 1).Select(n => $"CopyScheduler_FailedAdd_P{n}").ToArray();
            _timeoutMetricNames = Enumerable.Range(0, _assigner.MaxPriority + 1).Select(n => $"CopyScheduler_Timeout_P{n}").ToArray();
            _enqueuedMetricNames = Enumerable.Range(0, _assigner.MaxPriority + 1).Select(n => $"CopyScheduler_Enqueued_P{n}").ToArray();
            _executedMetricNames = Enumerable.Range(0, _assigner.MaxPriority + 1).Select(n => $"CopyScheduler_Executed_P{n}").ToArray();
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            // We use this instead of Task.Run in order to hint the scheduler that this will be a long running task, so
            // it won't block a thread on the thread pool.
            _schedulerTask = Task.Factory.StartNew(() =>
            {
                // We only take the tracing context out of the operation context because we don't want to cancel the
                // scheduling loop when this random context gets cancelled.
                var schedulerContext = new OperationContext(context, default);
                Scheduler(schedulerContext);
            }, TaskCreationOptions.LongRunning);

            // Ensure we always trace scheduler exceptions, even though they should never happen.
            _schedulerTask.FireAndForget(
                context,
                operation: "CopyScheduler",
                failureSeverity: Severity.Fatal,
                failFast: true);

            return BoolResult.SuccessTask;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            // The scheduler's task will be cancelled via the shutdown operation context, so we just need to wait for
            // it to complete.
            if (_schedulerTask != null)
            {
                // If the StartupAsync is called, we need to set the event to potentially unblock the Scheduler method that may be block
                // and waiting for new requests.
                _copyAddedEvent.Set();
                await _schedulerTask;
            }

            return BoolResult.Success;
        }

        public void Scheduler(OperationContext context)
        {
            using var trackingContext = TrackShutdown(context);
            var ctx = trackingContext.Context;

            Tracer.Info(ctx, "Copy scheduler starting");

            while (!ctx.Token.IsCancellationRequested)
            {
                if (Volatile.Read(ref _numPending) == 0)
                {
                    // There are no pending copies. Sleep until a new one is added.
                    _copyAddedEvent.Reset();

                    // WARNING: having a timeout here is essential to ensure that shutdown happens in a timely manner.
                    _copyAddedEvent.WaitOne(_configuration.MaximumEmptyCycleWait);
                    continue;
                }

                // Compute amount of copies to dispatch in this cycle
                var cycleQuota = ComputeCycleQuota();
                if (cycleQuota == 0)
                {
                    // There's no quota in this cycle. Sleep until the next transfer completes
                    _copyCompletedEvent.Reset();

                    // WARNING: having a timeout here is essential to ensure that shutdown happens in a timely manner.
                    _copyCompletedEvent.WaitOne(_configuration.MaximumEmptyCycleWait);
                    continue;
                }

                SchedulerCycle(ctx, cycleQuota).TraceIfFailure(ctx);
            }

            Tracer.Info(ctx, "Copy scheduler shutting down");

            // There's two kinds of copies that we need to deal with here:
            //  1. Inflight. These have already started, and are handled by the cancellation token that's passed into them.
            //  2. Pending. These are currently enqueued, and are waiting on the task source. We need to cancel them
            foreach (var priority in ComputePriorityTraversalOrder())
            {
                var candidates = _pending[priority];
                while (candidates.TryTake(out var pending))
                {
                    pending.TaskSource.SetCanceled();
                }
            }
        }

        public Result<SchedulerCycleMetadata> SchedulerCycle(OperationContext context, int cycleQuota)
        {
            // WARNING: This used to have a PerformOperation on the context, but that led to an enormous amount of MDM
            // counts being generated. Please avoid doing that. With this approach, errors are traced in the Scheduler
            // function above.
            try
            {
                return SchedulerCycleCore(context, cycleQuota);
            }
            catch (Exception e)
            {
                return new Result<SchedulerCycleMetadata>(e, $"Failed to complete scheduler cycle with quota `{cycleQuota}`");
            }
        }

        public struct SchedulerCycleMetadata
        {
            public int CycleQuota;

            public int CycleLeftover;

            public int NumInflightAtStart;

            public int NumInflightAtEnd;

            public int NumPendingAtStart;

            public int NumPendingAtEnd;

            public SchedulerCycleMetadata(int maxPriority)
                : this()
            {
                Contract.Requires(maxPriority > 0);
            }

            public override string ToString()
            {
                return
                    $"{nameof(CycleQuota)}=[{CycleQuota}] " +
                    $"{nameof(CycleLeftover)}=[{CycleLeftover}] " +
                    $"{nameof(NumInflightAtStart)}=[{NumInflightAtStart}] " +
                    $"{nameof(NumInflightAtEnd)}=[{NumInflightAtEnd}] " +
                    $"{nameof(NumPendingAtStart)}=[{NumPendingAtStart}] " +
                    $"{nameof(NumPendingAtEnd)}=[{NumPendingAtEnd}]";
            }
        }

        private Result<SchedulerCycleMetadata> SchedulerCycleCore(OperationContext context, int cycleQuota)
        {
            var state = new SchedulerCycleMetadata(_assigner.MaxPriority)
            {
                CycleQuota = cycleQuota,
                CycleLeftover = cycleQuota,
                NumPendingAtStart = _numPending,
                NumInflightAtStart = _numInflight,
            };

            // Traverse through each list of candidates, and dispatch as many copies as we can from each candidate
            // list by decreasing priority.
            foreach (var priority in ComputePriorityTraversalOrder())
            {
                if (state.CycleLeftover <= 0)
                {
                    break;
                }

                var candidates = _pending[priority];

                // Compute number of candidates to dispatch from this queue, and do so
                var maxPriorityQuota = ComputePriorityQuota(priority, state.CycleQuota, state.CycleLeftover, candidates.Count);

                for (var priorityQuota = maxPriorityQuota; priorityQuota > 0; priorityQuota--)
                {
                    if (!candidates.TryTake(out var candidate))
                    {
                        // We fall into this case whenever there aren't any candidates, so we just move forward
                        continue;
                    }

                    // WARNING: Order is important between this if and the Interlocked.Exchange. If they are not in
                    // this order, it is possible for a copy to have been dequeued and be cancelled due to timeout at
                    // the same time.
                    if (candidate.TimeoutToken.IsCancellationRequested || candidate.CancelToken.IsCancellationRequested)
                    {
                        candidate.TaskSource.SetCanceled();
                        continue;
                    }

                    Interlocked.Exchange(ref candidate.Dequeued, 1);

                    var request = candidate.Request;

                    var summary = new CopySchedulingSummary(
                        QueueWait: candidate.Stopwatch.Elapsed,
                        PriorityQueueLength: candidates.Count,
                        Priority: priority,
                        OverallQueueLength: _numPending);

                    if (request is OutboundPullCopy outboundPullRequest)
                    {
                        Execute(
                            context,
                            candidate,
                            (nestedContext) => outboundPullRequest.PerformOperationAsync(new OutboundPullArguments(nestedContext, summary)));
                    }
                    else if (request is OutboundPushCopyBase outboundPushRequestBase)
                    {
                        Execute(
                            context,
                            candidate,
                            (nestedContext) => outboundPushRequestBase.PerformOperationInternalAsync(new OutboundPushArguments(nestedContext, summary)));
                    }
                    else
                    {
                        // There is only one rule, and it is that the scheduler never stops
                        Tracer.Error(candidate.Request.Context, $"Attempt to satisfy a request with unhandled type `{request.GetType()}`");
                        continue;
                    }

                    Tracer.TrackMetric(context, _executedMetricNames[priority], 1);
                    state.CycleLeftover--;
                }
            }

            state.NumInflightAtEnd = _numInflight;
            state.NumPendingAtEnd = _numPending;

            return state;
        }

        private void Execute<T>(OperationContext context, CopyTask candidate, Func<OperationContext, Task<T>> taskFactory)
        {
            // We do it this way in order to avoid blocking the scheduler's thread with any synchronous part the
            // task factory may have.
            Task.Run(() =>
            {
                var priority = candidate.Priority;
                var nestedContext = new OperationContext(candidate.Request.Context, candidate.CancelToken);

                Task<T>? copyTask = null;
                Exception? factoryException = null;
                try
                {
                    copyTask = taskFactory(nestedContext);
                }
                catch (Exception e)
                {
                    factoryException = e;
                }

                // Order is important in the increments and decrements here. These guarantee that the number of inflight
                // is always an overestimate w.r.t. inflight by priority.
                Interlocked.Decrement(ref _numPendingByPriority[priority]);
                Interlocked.Decrement(ref _numPending);

                if (copyTask is null)
                {
                    // It may happen that the externally-provided task factory throws. In such a case, we'll fail the copy
                    // with this exception, which should be re-thrown when the user awaits for the copy to complete.
                    candidate.TaskSource.TrySetException(factoryException);
                    return;
                }

                // WARNING: the number if inflight operations is always an underestimate of the real number of inflight
                // operations, the reason being that there is no way to force the task's execution to start after this
                Interlocked.Increment(ref _numInflight);

                copyTask.ContinueWith(antecedent =>
                {
                    Interlocked.Decrement(ref _numInflight);

                    // This must happen at this point in order to ensure that we don't recompute quota without taking into
                    // consideration that this particular copy completed
                    _copyCompletedEvent.Set();

                    candidate.TaskSource.TrySetFromTask(antecedent, result => result!);
                }).FireAndForget(context);
            }, context.Token).FireAndForget(context);
        }

        private int ComputeCycleQuota()
        {
            var availableQuota = _configuration.MaximumConcurrentCopies - Volatile.Read(ref _numInflight);
            if (availableQuota <= 0)
            {
                return 0;
            }

            // Strategy here is to always leave some number of copies for the next loop, unless we don't really have
            // much space to do so.
            if (availableQuota <= _configuration.MinimumCapacityToAllowReservation)
            {
                return availableQuota;
            }

            return (int)Math.Ceiling(availableQuota * (1.0 - _configuration.ReservedCapacityPerCycleRate));
        }

        public IEnumerable<int> ComputePriorityTraversalOrder()
        {
            switch (_configuration.PriorityQuotaStrategy)
            {
                case PriorityQuotaStrategy.Even:
                    return Enumerable.Range(0, _pending.Length);
                default:
                    return Enumerable.Range(0, _pending.Length).Reverse();
            }
        }

        // TODO: test this function
        private int ComputePriorityQuota(int priority, int cycleQuota, int cycleLeftover, int numAvailableCandidates)
        {
            Contract.Requires(0 <= priority && priority <= _assigner.MaxPriority);
            Contract.Requires(0 < cycleQuota);
            Contract.Requires(0 < cycleLeftover);
            Contract.Requires(0 <= numAvailableCandidates);

            if (numAvailableCandidates <= 0)
            {
                // If there aren't any copies to be done, then the quota is 0
                return 0;
            }

            int allowedQuota;
            switch (_configuration.PriorityQuotaStrategy)
            {
                case PriorityQuotaStrategy.Default:
                case PriorityQuotaStrategy.FixedRate:
                    allowedQuota = (int)Math.Ceiling(cycleLeftover * _configuration.PriorityQuotaFixedRate);
                    break;
                case PriorityQuotaStrategy.Even:
                    // Evenly distributes the remaining load of the cycle among the priorities that have yet to be
                    // visited. This strategy is different from the others because it also traverses priorities in
                    // inverse order. This means that the highest priority queues will tend to get the most quota per
                    // cycle, since they'll get a portion of the quota plus a large portion of the leftovers
                    allowedQuota = (int)Math.Ceiling(cycleLeftover / ((double)((_assigner.MaxPriority - priority) + 1)));
                    break;
                case PriorityQuotaStrategy.CycleLeftover:
                    // Schedule as many copies as possible from the current priority class. This is equivalent to the
                    // FixedRate strategy with rate=1. Mostly here for convenience.
                    allowedQuota = cycleLeftover;
                    break;
                default:
                    throw new NotImplementedException($"Attempt to compute priority quota with an unknown strategy `{_configuration.PriorityQuotaStrategy}`");
            }

            // The effective amount that we can actually pull from the queue is bounded above by these numbers
            return Math.Min(Math.Min(cycleLeftover, numAvailableCandidates), allowedQuota);
        }

        /// <inheritdoc />
        public Task<CopySchedulerResult<CopyFileResult>> ScheduleOutboundPullAsync(OutboundPullCopy request)
        {
            return EnqueueAsync<CopyFileResult>(request);
        }

        /// <inheritdoc />
        public Task<CopySchedulerResult<T>> ScheduleOutboundPushAsync<T>(OutboundPushCopy<T> request)
            where T : class
        {
            return EnqueueAsync<T>(request);
        }

        private async Task<CopySchedulerResult<T>> EnqueueAsync<T>(CopyOperationBase request)
            where T : class
        {
            if (ShutdownStartedCancellationToken.IsCancellationRequested)
            {
                return CopySchedulerResult<T>.Shutdown();
            }

            using var schedulingTimeoutCts = new CancellationTokenSource(delay: _configuration.SchedulerTimeout ?? Timeout.InfiniteTimeSpan);
            using var cancelCopyCts = CancellationTokenSource.CreateLinkedTokenSource(request.Context.Token, ShutdownStartedCancellationToken);

            var priority = _assigner.Prioritize(request);
            Tracer.TrackMetric(request.Context, _createdMetricNames[priority], 1);

            if (IsImmediateRejectionCandidate(request) && _numPendingByPriority[priority] > _configuration.MaximumPendingUntilThrottle)
            {
                Tracer.TrackMetric(request.Context, _throttledMetricNames[priority], 1);
                return CopySchedulerResult<T>.Throttle(ThrottleReason.QueueTooLong);
            }

            var copy = new CopyTask(request, priority, schedulingTimeoutCts.Token, cancelCopyCts.Token);
            Interlocked.Increment(ref _numPending);
            Interlocked.Increment(ref _numPendingByPriority[priority]);

            var addResult = _pending[priority].TryAdd(copy);
            if (!addResult)
            {
                // We failed to add, so the scheduler won't be updating these numbers and we need to do so manually
                Interlocked.Decrement(ref _numPending);
                Interlocked.Decrement(ref _numPendingByPriority[priority]);

                Tracer.TrackMetric(request.Context, _failedAddMetricNames[priority], 1);
                return new CopySchedulerResult<T>(
                    reason: SchedulerFailureCode.Unknown,
                    errorMessage: $"{nameof(PrioritizedCopyScheduler)} failed to enqueue request");
            }
            Tracer.TrackMetric(request.Context, _enqueuedMetricNames[priority], 1);

            _copyAddedEvent.Set();

            // At this point, we have added the copy task to its appropriate priority queue and we need to wait for it
            // to be dispatched and completed.
            var resultTask = copy.TaskSource.Task;

            // The copy task can be abandoned in case of cancellation. Observing the error to avoid unobserved task errors.
            resultTask.Forget();
            using (var schedulerTimeoutAwaiter = schedulingTimeoutCts.Token.ToAwaitable())
            using (var cancelCopyAwaiter = cancelCopyCts.Token.ToAwaitable())
            {
                _ = await Task.WhenAny(resultTask, schedulerTimeoutAwaiter.CompletionTask, cancelCopyAwaiter.CompletionTask);
                if (resultTask.IsCompleted)
                {
                    var result = await resultTask;
                    return new CopySchedulerResult<T>((T)result);
                }
            }

            if (cancelCopyCts.Token.IsCancellationRequested)
            {
                if (ShutdownStartedCancellationToken.IsCancellationRequested)
                {
                    // cancelCopyCts fired because shutdown has started
                    return CopySchedulerResult<T>.Shutdown();
                }

                if (request.Context.Token.IsCancellationRequested)
                {
                    // cancelCopyCts fired because the copy was externally cancelled

                    // We throw here instead of returning a cancelled result intentionally, because callers may have
                    // special logic for handling cancellations
                    request.Context.Token.ThrowIfCancellationRequested();
                }

                throw Contract.AssertFailure("Never supposed to happen");
            }

            // If we are here, it means that the copy wasn't cancelled, but the scheduler timeout fired.
            Contract.Assert(schedulingTimeoutCts.Token.IsCancellationRequested);

            if (Volatile.Read(ref copy.Dequeued) == 0)
            {
                // The copy is still waiting in the queue

                // WARNING: There is a race condition here:
                //  - The copy gets dequeued, checks for the timeout but it hasn't happened, then the scheduling thread
                //    gets preempted for a while.
                //  - In the meantime, the timeout fires, and we enter this if (because the scheduler hasn't marked the
                //    copy as dequeued yet). We get preempted right before the next line executes.
                //  - The scheduler marks the copy as dequeued, and starts running the copy.
                //  - At this point, we have basically accepted that the copy is cancelled due to scheduler timeout,
                //    but the copy has actually started running.
                // This next line will actually cancel that copy.
                //
                // Note that this race condition should be uncommon, because it depends on the timeout firing at a very
                // precise time (basically equivalent to the amount of time it takes the copy scheduler to get to the
                // copy).
                cancelCopyCts.Cancel();

                Tracer.TrackMetric(request.Context, _timeoutMetricNames[priority], 1);
                return CopySchedulerResult<T>.TimeOut(_configuration.SchedulerTimeout);
            }

            using (var cancelCopyAwaiter = cancelCopyCts.Token.ToAwaitable())
            {
                _ = await Task.WhenAny(resultTask, cancelCopyAwaiter.CompletionTask);
            }

            if (resultTask.IsCompleted)
            {
                var result = await resultTask;
                return new CopySchedulerResult<T>((T)result);
            }

            Contract.Assert(cancelCopyCts.Token.IsCancellationRequested);

            if (ShutdownStartedCancellationToken.IsCancellationRequested)
            {
                // cancelCopyCts fired because shutdown has started
                return CopySchedulerResult<T>.Shutdown();
            }

            if (request.Context.Token.IsCancellationRequested)
            {
                // cancelCopyCts fired because the copy was externally cancelled

                // We throw here instead of returning a cancelled result intentionally, because callers may have
                // special logic for handling cancellations
                request.Context.Token.ThrowIfCancellationRequested();
            }

            throw Contract.AssertFailure("Never supposed to happen");
        }

        private static bool IsImmediateRejectionCandidate(CopyOperationBase request)
        {
            switch (request.Reason)
            {
                case CopyReason.None:
                case CopyReason.ProactiveBackground:
                    return true;
                case CopyReason.ProactiveCopyOnPut:
                case CopyReason.AsyncCopyOnPin:
                case CopyReason.CentralStorage:
                case CopyReason.ProactiveCopyOnPin:
                case CopyReason.OpenStream:
                case CopyReason.Place:
                case CopyReason.Pin:
                case CopyReason.ProactiveCheckpointCopy:
                    return false;
                default:
                    throw new NotImplementedException($"Attempt to compute immediate rejection for a request with type `{request.GetType()}` and reason `{request.Reason}` is unhandled");
            }
        }


    }
}
