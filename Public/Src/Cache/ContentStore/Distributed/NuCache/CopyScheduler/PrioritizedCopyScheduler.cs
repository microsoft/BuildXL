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
using BuildXL.Cache.ContentStore.Interfaces.Time;
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

            public CopyTask(CopyOperationBase request, int priority)
            {
                Request = request;
                Priority = priority;
                Stopwatch = StopwatchSlim.Start();
            }
        }

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(PrioritizedCopyScheduler));

        private readonly PrioritizedCopySchedulerConfiguration _configuration;
        private readonly IClock _clock;

        private readonly IProducerConsumerCollection<CopyTask>[] _pending;

        private int _numPending;
        private readonly int[] _numPendingByPriority;

        private int _numInflight;
        private readonly int[] _numInflightByPriority;

        private readonly ICopySchedulerPriorityAssigner _assigner;

        // NOTE: we don't dispose of this field because copies may take some amount of time to cancel, so if we did
        // that we could end up throwing ObjectDisposedException on copies when shutting down.
        private readonly EventWaitHandle _copyCompletedEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset);

        /// <nodoc />
        public PrioritizedCopyScheduler(PrioritizedCopySchedulerConfiguration configuration, IClock clock)
        {
            Contract.Requires(configuration.MaximumConcurrentCopies > 0);
            Contract.Requires(configuration.ReservedCapacityPerCycleRate >= 0 && configuration.ReservedCapacityPerCycleRate < 1);

            _configuration = configuration;
            _clock = clock;

            switch (configuration.PriorityAssignmentStrategy)
            {
                case PrioritizedCopySchedulerPriorityAssignmentStrategy.Default:
                    _assigner = new DefaultPriorityAssigner();
                    break;
                default:
                    throw new NotImplementedException($"Unsupported {nameof(PrioritizedCopySchedulerPriorityAssignmentStrategy)} value {configuration.PriorityAssignmentStrategy}");
            }

            _numPendingByPriority = new int[_assigner.MaxPriority + 1];
            _numInflightByPriority = new int[_assigner.MaxPriority + 1];

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
        }

        private Task? _schedulerTask;

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            // We use this instead of Task.Run in order to hint the scheduler that this will be a long running task, so
            // it won't block a thread on the thread pool.
            _schedulerTask = Task.Factory.StartNew(() =>
            {
                // We don't use TrackShutdown on the provided context because we want to ensure that the scheduler's
                // cancellation is only dependent on the class being shutdown instead of some random cancellation token
                // that we don't control.
                var schedulerContext = new OperationContext(context, ShutdownStartedCancellationToken);
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
                await _schedulerTask;
            }

            return BoolResult.Success;
        }

        public void Scheduler(OperationContext context)
        {
            Tracer.Info(context, "Copy scheduler starting");

            while (!context.Token.IsCancellationRequested)
            {
                // Compute amount of copies to dispatch in this cycle
                var cycleQuota = ComputeCycleQuota();
                if (cycleQuota == 0)
                {
                    // There's no quota in this cycle. Sleep until the next transfer completes
                    Tracer.Info(context, $"No quota available for current cycle. Sleeping for next copy completion or `{_configuration.MaximumEmptyCycleWait}`");
                    _copyCompletedEvent.Reset();
                    _copyCompletedEvent.WaitOne(_configuration.MaximumEmptyCycleWait);
                    continue;
                }

                SchedulerCycle(context, cycleQuota).TraceIfFailure(context);
            }

            // There is no need for us to do any kind of shutdown logic here. Copies' cancellation token will be
            // triggered, so they'll prune themselves out as they complete.
            Tracer.Info(context, "Copy scheduler shutting down");
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

        public record SchedulerCycleMetadata
        {
            public int CycleQuota;

            public int CycleLeftover;

            public int[] PriorityQuota;

            public int[] ScheduledByPriority;

            public int NumInflightAtStart;

            public int NumInflightAtEnd;

            public int NumPendingAtStart;

            public int NumPendingAtEnd;

            public SchedulerCycleMetadata(int maxPriority)
            {
                Contract.Requires(maxPriority > 0);
                PriorityQuota = new int[maxPriority + 1];
                ScheduledByPriority = new int[maxPriority + 1];
            }

            public override string ToString()
            {
                var quotaString = string.Join(" ", PriorityQuota.Select((value, index) => $"Quota{index}=[{value}]"));
                var scheduledString = string.Join(" ", ScheduledByPriority.Select((value, index) => $"Scheduled{index}=[{value}]"));
                return
                    $"{nameof(CycleQuota)}=[{CycleQuota}] " +
                    $"{nameof(CycleLeftover)}=[{CycleLeftover}] " +
                    $"{nameof(NumInflightAtStart)}=[{NumInflightAtStart}] " +
                    $"{nameof(NumInflightAtEnd)}=[{NumInflightAtEnd}] " +
                    $"{nameof(NumPendingAtStart)}=[{NumPendingAtStart}] " +
                    $"{nameof(NumPendingAtEnd)}=[{NumPendingAtEnd}] {quotaString} {scheduledString}";
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
                state.PriorityQuota[priority] = ComputePriorityQuota(priority, state.CycleQuota, state.CycleLeftover, candidates.Count);
                state.ScheduledByPriority[priority] = state.CycleLeftover;

                for (var priorityQuota = state.PriorityQuota[priority]; priorityQuota > 0; priorityQuota--)
                {
                    if (!candidates.TryTake(out var candidate))
                    {
                        // We fall into this case whenever there aren't any candidates, so we just move forward
                        continue;
                    }

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

                    Tracer.TrackMetric(context, $"CopyScheduler_Executed_P{priority}", 1);

                    state.CycleLeftover--;
                }

                state.ScheduledByPriority[priority] -= state.CycleLeftover;
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

                // Here we merge the cycle's cancellation token with the scheduler's shutdown token and the copy's
                // cancellation token. Ensures that the copy that'll start next will be cancelled appropriately.
                var cts = CancellationTokenSource.CreateLinkedTokenSource(
                    ShutdownStartedCancellationToken,
                    context.Token,
                    candidate.Request.Context.Token);
                var nestedContext = new OperationContext(candidate.Request.Context, cts.Token);

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
                Interlocked.Increment(ref _numInflightByPriority[priority]);

                copyTask.ContinueWith(antecedent =>
                {
                    Interlocked.Decrement(ref _numInflightByPriority[priority]);
                    Interlocked.Decrement(ref _numInflight);

                    // This must happen at this point in order to ensure that we don't recompute quota without taking into
                    // consideration that this particular copy completed
                    _copyCompletedEvent.Set();

                    candidate.TaskSource.TrySetFromTask(antecedent, result => result!);
                    cts.Dispose();
                }).FireAndForget(context);
            }, context.Token).FireAndForget(context);
        }

        private int ComputeCycleQuota()
        {
            var availableQuota = _configuration.MaximumConcurrentCopies - _numInflight;
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
            var priority = _assigner.Prioritize(request);
            Tracer.TrackMetric(request.Context, $"CopyScheduler_Created_P{priority}", 1);

            if (IsImmediateRejectionCandidate(request) && _numPendingByPriority[priority] > _configuration.MaximumPendingUntilRejection)
            {
                Tracer.TrackMetric(request.Context, $"CopyScheduler_Rejected_P{priority}", 1);
                return CopySchedulerResult<T>.Reject(ImmediateRejectionReason.QueueTooLong);
            }

            var copy = new CopyTask(request, priority);
            var addResult = _pending[priority].TryAdd(copy);
            if (!addResult)
            {
                return new CopySchedulerResult<T>(
                    reason: SchedulerFailureCode.Unknown,
                    errorMessage: "Failed to enqueue request");
            }

            Interlocked.Increment(ref _numPending);
            Interlocked.Increment(ref _numPendingByPriority[priority]);

            Tracer.TrackMetric(request.Context, $"CopyScheduler_Enqueued_P{priority}", 1);
            var result = await copy.TaskSource.Task;
            return new CopySchedulerResult<T>((result as T)!);
        }

        private static bool IsImmediateRejectionCandidate(CopyOperationBase request)
        {
            switch (request.Reason)
            {
                case CopyReason.None:
                case CopyReason.ProactiveBackground:
                case CopyReason.ProactiveCopyOnPut:
                    return true;
                case CopyReason.AsyncCopyOnPin:
                case CopyReason.CentralStorage:
                case CopyReason.ProactiveCopyOnPin:
                case CopyReason.OpenStream:
                case CopyReason.Place:
                case CopyReason.Pin:
                    return false;
                default:
                    throw new NotImplementedException($"Attempt to compute immediate rejection for a request with type `{request.GetType()}` and reason `{request.Reason}` is unhandled");
            }
        }


    }
}
