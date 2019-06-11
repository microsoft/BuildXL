// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// The entity that maintains and enforces the content quota.
    /// </summary>
    public sealed partial class QuotaKeeperV2 : QuotaKeeper
    {
        private readonly CounterCollection<QuotaKeeperCounters> _counters;

        private readonly ContentStoreInternalTracer _contentStoreTracer;
        private readonly Tracer _tracer;

        private readonly CancellationToken _token;

        private readonly IContentStoreInternal _store;

        private readonly BlockingCollection<QuotaRequest> _reserveQueue;

        private readonly ConcurrentQueue<ReserveSpaceRequest> _evictionQueue;
        private long _evictionQueueSize;
        private readonly object _evictionLock = new object();

        private readonly List<IQuotaRule> _rules;

        /// <summary>
        /// The size of the entire content on disk.
        /// </summary>
        private long _allContentSize;

        /// <summary>
        /// The size of requested content (incremented at <see cref="ReserveAsync(long)"/> and decremented when the transaction is committed).
        /// </summary>
        private long _requestedSize;

        /// <summary>
        /// The size of the reserved content (incremented in <see cref="OnContentEvicted"/> and decremented when the transaction returned from <see cref="ReserveAsync(long)"/> is committed).
        /// This size is used for completing reservation requests that wait for a free space as soon as enough content is evicted.
        /// </summary>
        private long _reservedSize;

        /// <summary>
        /// Global long-running task to process all incoming requests.
        /// </summary>
        private Task _processReserveRequestsTask;

        private readonly DistributedEvictionSettings _distributedEvictionSettings;
        private Task<PurgeResult> _purgeTask;
        private readonly object _purgeTaskLock = new object();

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <inheritdoc />
        public override CounterCollection<QuotaKeeperCounters> Counters => _counters;

        /// <nodoc />
        public QuotaKeeperV2(
            IAbsFileSystem fileSystem,
            ContentStoreInternalTracer tracer,
            QuotaKeeperConfiguration configuration,
            CancellationToken token,
            IContentStoreInternal store)
        : base(configuration)
        {
            _contentStoreTracer = tracer;
            _tracer = new Tracer(name: Component);
            _allContentSize = configuration.ContentDirectorySize;
            _token = token;
            _store = store;
            _reserveQueue = new BlockingCollection<QuotaRequest>();
            _evictionQueue =  new ConcurrentQueue<ReserveSpaceRequest>();
            _distributedEvictionSettings = configuration.DistributedEvictionSettings;
            _rules = CreateRules(fileSystem, configuration, store);
            _counters = new CounterCollection<QuotaKeeperCounters>();
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            // Potential error is already traced by Calibrate method. Ignore the result to avoid double reporting.
            await CalibrateAllAsync(context).IgnoreFailure();

            // Processing requests is a long running operation. Scheduling it into a dedicated thread to avoid thread pool exhaustion.
            _processReserveRequestsTask = Task.Factory.StartNew(
                () => ProcessReserveRequestsAsync(context.CreateNested()),
                TaskCreationOptions.LongRunning).Unwrap();

            if (PurgeAtStartup)
            {
                // Start purging immediately on startup to clear out residual content in the cache
                // over the cache quota if configured.
                const string Operation = "PurgeRequest";
                SendPurgeRequest(context, "Startup").FireAndForget(context, Operation);
            }
            else
            {
                _tracer.Debug(context, $"{_tracer.Name}: do not purge at startup based on configuration settings.");
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            // Need to notify the requests queue that there would be no more new requests.
            _reserveQueue.CompleteAdding();

            if (_processReserveRequestsTask != null)
            {
                context.TraceDebug($"{_tracer.Name}: waiting for pending reservation requests.");
                await _processReserveRequestsTask;
            }

            if (_purgeTask != null)
            {
                context.TraceDebug($"{_tracer.Name}: waiting for purge task.");
                return await _purgeTask;
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        public override void Calibrate()
        {
            if (_rules.Any(r => r.CanBeCalibrated))
            {
                var request = QuotaRequest.Calibrate();
                _reserveQueue.Add(request);
            }
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            base.DisposeCore();

            _reserveQueue.Dispose();
        }

        /// <inheritdoc />
        public override Task SyncAsync(Context context, bool purge)
        {
            var operationContext = new OperationContext(context);

            return operationContext.PerformOperationAsync(
                _tracer,
                () => syncCoreAsync());

            async Task<BoolResult> syncCoreAsync()
            {
                var purgeTask = _purgeTask;
                if (purgeTask != null)
                {
                    await purgeTask.ThrowIfFailure();
                }

                // Ensure there are no pending requests.
                await SendSyncRequest(operationContext, purge).ThrowIfFailure();

                purgeTask = _purgeTask;
                if (purgeTask != null)
                {
                    await purgeTask.ThrowIfFailure();
                }

                return BoolResult.Success;
            }
        }

        private Task<BoolResult> SendPurgeRequest(OperationContext context, string reason)
        {
            return context.PerformOperationAsync(
                _tracer,
                () =>
                {
                    var emptyRequest = QuotaRequest.Purge();
                    _reserveQueue.Add(emptyRequest);

                    return emptyRequest.CompletionAsync();
                },
                extraStartMessage: $"Requesting purge due to '{reason}'.");
        }

        private Task<BoolResult> SendSyncRequest(OperationContext context, bool purge)
        {
            if (purge)
            {
                return SendPurgeRequest(context, "Sync");
            }

            return context.PerformOperationAsync(
                _tracer,
                () =>
                {
                    var emptyRequest = QuotaRequest.Synchronize();
                    _reserveQueue.Add(emptyRequest);

                    return emptyRequest.CompletionAsync();
                });
        }

        /// <inheritdoc />
        public override async Task<ReserveTransaction> ReserveAsync(long contentSize)
        {
            Contract.Assert(contentSize >= 0);

            ShutdownStartedCancellationToken.ThrowIfCancellationRequested();

            var reserveRequest = QuotaRequest.Reserve(contentSize);

            // To avoid potential race condition need to increase size first and only after that to add the request into the queue.
            IncreaseSize(ref _requestedSize, reserveRequest.ReserveSize);
            _reserveQueue.Add(reserveRequest);

            BoolResult result = await reserveRequest.CompletionAsync();

            if (!result)
            {
                throw new CacheException($"Failed to reserve space for content size=[{contentSize}], result=[{result}]");
            }

            return new ReserveTransaction(reserveRequest, OnReservationCommitted);
        }

        /// <inheritdoc />
        protected internal override async Task<EvictResult> EvictContentAsync(Context context, ContentHashWithLastAccessTimeAndReplicaCount contentHashInfo, bool onlyUnlinked)
        {
            var evictResult = await _store.EvictAsync(context, contentHashInfo, onlyUnlinked, evicted: null);
            if (evictResult.SuccessfullyEvictedHash)
            {
                OnContentEvicted(evictResult.EvictedSize);
            }

            return evictResult;
        }

        private async Task<BoolResult> CalibrateAllAsync(Context context)
        {
            BoolResult result = BoolResult.Success;
            foreach (var rule in _rules.Where(r => r.CanBeCalibrated))
            {
                var calibrationResult = await CalibrateAsync(context, rule);
                if (!calibrationResult)
                {
                    result &= calibrationResult;
                }
            }

            return result;
        }

        private Task<CalibrateResult> CalibrateAsync(Context context, IQuotaRule rule)
        {
            var operationContext = new OperationContext(context, _token);
            return operationContext.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    if (!rule.CanBeCalibrated)
                    {
                        return CalibrateResult.CannotCalibrate;
                    }

                    if (ShouldAbortOperation(context, "Calibrate", out var reason))
                    {
                        return new CalibrateResult(reason);
                    }

                    return await rule.CalibrateAsync();
                });
        }

        private bool ShouldAbortOperation(Context context, string operation, out string reason)
        {
            reason = null;

            if (_token.IsCancellationRequested)
            {
                reason = $"{operation} exiting due to shutdown.";
                _tracer.Debug(context, reason);
            }

            return reason != null;
        }

        /// <inheritdoc />
        public override long CurrentSize => Thread.VolatileRead(ref _allContentSize);

        /// <summary>
        /// Helper method for handling <see cref="QuotaRequest"/>
        /// </summary>
        private async Task<BoolResult> ProcessQuotaRequestAsync(Context context, QuotaRequest request)
        {
            if (request is CalibrateQuotaRequest)
            {
                return await CalibrateAllAsync(context);
            }

            if (request is SynchronizationRequest)
            {
                // Do nothing for synchronization.
                return BoolResult.Success;
            }

            var reserveQuota = request as ReserveSpaceRequest;
            Contract.Assert(reserveQuota != null);

            // Here is a reservation logic
            // 1. If above hard limit
            //    "wait" for eviction to free enough space to fit new content
            //    Complete the request if eviction is successful
            // 2. If above soft limit
            //    Start the eviction if needed (and print message why the eviction should start)
            //    Compete the request
            // 3. Below soft limit
            //    Complete the request

            var reserveSize = reserveQuota.ReserveSize;

            if (IsAboveHardLimit(reserveSize, out var exceedReason))
            {
                // Now we should wait for eviction until enough space is freed.
                var evictionResult = await EvictUntilTheHardLimitAsync(context, reserveSize, exceedReason);

                if (evictionResult)
                {
                    // Eviction was successful, need to track it to reduce the reserved size that was increased by eviction.
                    reserveQuota.IsReservedFromEviction = true;
                }
                else
                {
                    _tracer.Debug(context, $"{Component}: EvictUntilTheHardLimitAsync failed with " + evictionResult);
                    // Eviction may fail, but this could be fine if all the rules that above the hard limit can be calibrated.
                    return SuccessIfOnlyCalibrateableRulesAboveHardLimit(context, reserveSize);
                }

                return evictionResult;
            }

            if (IsAboveSoftLimit(reserveSize, out exceedReason))
            {
                StartPurgeIfNeeded(context, reason: $"soft limit surpassed. {exceedReason}");
            }

            return BoolResult.Success;
        }

        private BoolResult SuccessIfOnlyCalibrateableRulesAboveHardLimit(Context context, long reserveSize)
        {
            var rulesNotInsideHardLimit = _rules.Where(rule => !rule.IsInsideHardLimit(reserveSize).Succeeded).ToList();

            var rulesCannotBeCalibratedResults =
                rulesNotInsideHardLimit.Where(rule => !rule.CanBeCalibrated)
                    .Select(rule => rule.IsInsideHardLimit(reserveSize))
                    .ToList();

            if (rulesCannotBeCalibratedResults.Any())
            {
                // Some rule has reached its hard limit, and its quota cannot be calibrated.
                var sb = new StringBuilder();

                sb.AppendLine("Error: Failed to make space.")
                  .AppendLine($"Current size={CurrentSize}.");

                foreach (var ruleResult in rulesCannotBeCalibratedResults)
                {
                    sb.AppendLine($"Hard limit surpassed. {ruleResult.ErrorMessage}");
                }

                return new BoolResult(sb.ToString());
            }

            // All rules that reached their hard limits can be calibrated. We will disable such rules temporarily until calibration.
            foreach (var rule in rulesNotInsideHardLimit.Where(rule => rule.CanBeCalibrated))
            {
                _tracer.Debug(context, $"Disabling rule '{rule}'.");
                rule.IsEnabled = false;
            }

            return BoolResult.Success;
        }

        private bool IsAboveSoftLimit(long size, out string exceedReason)
        {
            foreach (var rule in _rules)
            {
                var checkResult = rule.IsInsideSoftLimit(size);
                if (!checkResult)
                {
                    exceedReason = checkResult.ErrorMessage;
                    return true;
                }
            }

            exceedReason = null;
            return false;
        }

        private bool IsAboveHardLimit(long size, out string exceedReason)
        {
            foreach (var rule in _rules)
            {
                var checkResult = rule.IsInsideHardLimit(size);
                if (!checkResult)
                {
                    exceedReason = checkResult.ErrorMessage;
                    return true;
                }
            }

            exceedReason = null;
            return false;
        }

        /// <inheritdoc />
        internal override bool StopPurging(out string stopReason, out IQuotaRule activeRule)
        {
            activeRule = null;
            var reserveSize = _requestedSize;

            if (_token.IsCancellationRequested)
            {
                stopReason = "cancellation requested";
                return true;
            }

            foreach (var rule in _rules)
            {
                var isInsideTargetLimit = rule.IsInsideTargetLimit(reserveSize);

                if (!isInsideTargetLimit)
                {
                    activeRule = rule;
                    stopReason = null;
                    return false;
                }
            }

            stopReason = "inside target limit";
            return true;
        }

        private void StartPurgeIfNeeded(Context context, string reason)
        {
            if (_purgeTask == null)
            {
                lock (_purgeTaskLock)
                {
                    if (_purgeTask == null)
                    {
                        _tracer.Debug(context, $"{Component}: Purge stated because {reason}. Current Size={CurrentSize}");
                        _purgeTask = Task.Run(() => PurgeAsync(context));
                    }
                }
            }
        }

        /// <inheritdoc />
        public override void OnContentEvicted(long size)
        {
            DecreaseSize(ref _allContentSize, size);

            lock (_evictionLock)
            {
                // Track freed space to unblock reservations which could fit in evicted space.
                while (_evictionQueue.TryPeek(out var evictionRequest))
                {
                    IncreaseSize(ref _reservedSize, evictionRequest.ReserveSize);

                    // Tracking reserved size to prevent the following race:
                    // Hard Limit = 100, Current Size = 93, Eviction Requests: 9 and 9
                    // Evicted 3 bytes
                    // Current Size = 90, Evicted Size = 3, evictionRequest.ReserveSize = 9

                    // We should complete only the first request and keep the second one until the next eviction.
                    // To achieve this, we track the reserved size bytes inside (in _reservedSize) that is increased here
                    // and decreased once reservation transaction is committed.
                    if (IsAboveHardLimit(_reservedSize, out _))
                    {
                        // There is not enough content evicted. None of the requests can be completed. Need to wait for another eviction.
                        return;
                    }

                    if (_evictionQueue.TryDequeue(out _))
                    {
                        Interlocked.Decrement(ref _evictionQueueSize);
                    }

                    // Finishing eviction request to unblock the reservation request.
                    evictionRequest.Success();
                }
            }
        }

        private Task<BoolResult> EvictUntilTheHardLimitAsync(Context context, long reserveSize, string purgeReason)
        {
            // Hard limit surpassed.

            // Need to create eviction request first and then start purging if needed.
            // The order matters to avoid subtle race condition: purger needs to see the new request.
            var evictionRequest = QuotaRequest.Reserve(reserveSize);
            _evictionQueue.Enqueue(evictionRequest);
            Interlocked.Increment(ref _evictionQueueSize);

            StartPurgeIfNeeded(context, reason: $"hard limit surpassed. {purgeReason}");

            return evictionRequest.CompletionAsync();
        }

        private async Task<PurgeResult> PurgeAsync(Context context)
        {
            var operationContext = new OperationContext(context);
            var operationResult = await operationContext.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    var finalPurgeResult = new PurgeResult();
                    PurgeResult purgeResult = null;

                    do
                    {
                        purgeResult = await PurgeCoreAsync(operationContext);
                        if (purgeResult)
                        {
                            finalPurgeResult.Merge(purgeResult);
                        }
                    }
                    while (ContinuePurging(purgeResult));

                    // Saving current content size for tracing purposes.
                    finalPurgeResult.CurrentContentSize = CurrentSize;
                    return finalPurgeResult;
                },
                _counters[QuotaKeeperCounters.PurgeCall]);

            // Tests rely on the PurgeCount to be non-0.
            _contentStoreTracer.PurgeStop(context, operationResult);

            return operationResult;
        }

        private Task<PurgeResult> PurgeCoreAsync(OperationContext context)
        {
            // This operation must be exception safe, because otherwise QuotaKeeper will keep
            // unprocessed requests that may cause ShutdownAsync operation to hang forever.
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // Check for cancellation token and other reasons why to immediately stop the purge.
                    if (ShouldAbortOperation(context, "Purge", out var message))
                    {
                        // Error will force the purge loop to stop.
                        return new PurgeResult(message);
                    }
                    else
                    {
                        // Trying to purge the content
                        var contentHashesWithInfo = await _store.GetLruOrderedContentListWithTimeAsync();
                        var purger = CreatePurger(context, contentHashesWithInfo);
                        return await purger.PurgeAsync();
                    }
                },
                traceErrorsOnly: true);
        }

        private Purger CreatePurger(Context context, IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo)
        {
            long reserveSize = Interlocked.Read(ref _requestedSize);

            var purger = new Purger(
                context,
                this,
                _distributedEvictionSettings,
                contentHashesWithInfo,
                new PurgeResult(reserveSize, contentHashesWithInfo.Count, $"[{string.Join(", ", _rules.Select(r => r.Quota))}]"),
                _token);
            return purger;
        }

        private bool ContinuePurging(PurgeResult purgeResult)
        {
            lock (_purgeTaskLock)
            {
                if (purgeResult.EvictedFiles == 0)
                {
                    // Need to terminate all the eviction requests.
                    while (_evictionQueue.TryDequeue(out var request))
                    {
                        request.Failure($"Failed to free space. Eviction result: {purgeResult}.");
                    }
                }

                if (_evictionQueue.IsEmpty)
                {
                    _purgeTask = null;
                    return false;
                }
            }

            return true;
        }

        private async Task ProcessReserveRequestsAsync(Context context)
        {
            context.Debug($"{Component}: Starting reservation processing loop. Current content size={CurrentSize}");
            var operationContext = new OperationContext(context);

            foreach (var request in _reserveQueue.GetConsumingEnumerable())
            {
                try
                {
                    var result = await operationContext.PerformOperationAsync(
                        _tracer,
                        () => ProcessQuotaRequestAsync(context, request),
                        extraStartMessage: $"Request='{request}'.",
                        extraEndMessage: r => $"Request='{request}'. CurrentContentSize={CurrentSize}.",
                        caller: nameof(ProcessQuotaRequestAsync),
                        counter: _counters[QuotaKeeperCounters.ProcessQuotaRequest]);

                    if (result)
                    {
                        if (request is ReserveSpaceRequest reserveRequest)
                        {
                            // When the reservation succeeds, the reserved size should fit under the hard limit (unless sensitive sessions are presented).
                            if (IsAboveHardLimit(reserveRequest.ReserveSize, out var message))
                            {
                                Contract.Assert(false, $"Reservation request is successful but still below hard quota. {message}");
                            }
                        }

                        // The order matters here: we need to change the instance state first before completing the request.
                        request.Success();
                    }
                    else
                    {
                        request.Failure(result.ToString());
                    }
                }
                catch (Exception e)
                {
                    _tracer.Error(context, $"{Component}: Purge loop failed for '{request}' with unexpected error: {e}");
                }
            }
        }

        private void OnReservationCommitted(ReserveSpaceRequest reserveSpaceRequest)
        {
            // TODO: check that reservation was never called with -1 sizes.
            // TODO: add a comment that the order matters.
            IncreaseSize(ref _allContentSize, reserveSpaceRequest.ReserveSize);
            DecreaseSize(ref _requestedSize, reserveSpaceRequest.ReserveSize);

            if (reserveSpaceRequest.IsReservedFromEviction)
            {
                // If the request was completed because of eviction, then we should reduce the reserved size to
                // allow other requests to be completed during eviction.
                DecreaseSize(ref _reservedSize, reserveSpaceRequest.ReserveSize);
            }
        }

        private void DecreaseSize(ref long size, long delta)
        {
            Contract.Assert(delta >= 0);

            long newSize = Interlocked.Add(ref size, -1 * delta);

            Contract.Assert(newSize >= 0);
        }

        private void IncreaseSize(ref long size, long delta)
        {
            Contract.Assert(delta >= 0);

            Interlocked.Add(ref size, delta);
        }
    }
}
