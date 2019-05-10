// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Quota rules use this to evict content.
    /// </summary>
    public delegate Task<EvictResult> EvictAsync(Context context, ContentHashWithLastAccessTimeAndReplicaCount contentHashInfo, bool onlyUnlinked);

    /// <summary>
    /// Original implementation of a quota keeper.
    /// </summary>
    public sealed class LegacyQuotaKeeper : QuotaKeeper
    {
        private readonly ContentStoreInternalTracer _tracer;
        private readonly CancellationToken _token;
        private readonly IContentStoreInternal _store;
        private readonly BlockingCollection<Request<QuotaKeeperRequest, string>> _reserveQueue;
        private readonly SemaphoreSlim _contentItemEvicted = new SemaphoreSlim(0, int.MaxValue);
        private readonly List<IQuotaRule> _rules;
        private long _size;
        private Task<PurgeResult> _purgeTask;
        private Task _reserveTask;
        private BackgroundTaskTracker _taskTracker;
        private bool _usePreviousPinnedBytes;
        private long _previousPinnedBytes;

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <nodoc />
        public LegacyQuotaKeeper(
            IAbsFileSystem fileSystem,
            ContentStoreInternalTracer tracer,
            QuotaKeeperConfiguration configuration,
            CancellationToken token,
            IContentStoreInternal store)
        : base(configuration)
        {
            _tracer = tracer;
            _size = configuration.ContentDirectorySize;
            _token = token;
            _store = store;
            _reserveQueue = new BlockingCollection<Request<QuotaKeeperRequest, string>>();

            _rules = CreateRules(fileSystem, configuration, store);
        }

        /// <inheritdoc />
        public override long CurrentSize => Thread.VolatileRead(ref _size);

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            foreach (var quotaRule in _rules.Where(r => r.CanBeCalibrated))
            {
                await CalibrateAsync(context, quotaRule);
            }

            _reserveTask = Task.Run(() => Reserve(new Context(context)));
            _taskTracker = new BackgroundTaskTracker(Component, new Context(context));

            if (PurgeAtStartup)
            {
                // Start purging immediately on startup to clear out residual content in the cache
                // over the cache quota if configured.
                StartPurging();
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
            _reserveQueue.CompleteAdding();

            if (_reserveTask != null)
            {
                await _reserveTask;
            }

            if (_purgeTask != null)
            {
                try
                {
                    (await _purgeTask).ThrowIfFailure();
                }
                catch (Exception exception)
                {
                    _tracer.Warning(context, $"Exception from purger during shutdown: {exception}");
                }
            }

            if (_taskTracker != null)
            {
                await _taskTracker.Synchronize();
                await _taskTracker.ShutdownAsync(context);
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        internal override bool StopPurging(out string stopReason, out IQuotaRule activeRule)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        ///     Signal that a pin has been released, and thus the previously viewed pin count may be invalid.
        /// </summary>
        public void AnnouncePinRelease()
        {
            _usePreviousPinnedBytes = false;
        }

        /// <inheritdoc />
        public override void Calibrate()
        {
            if (_rules.Any(r => r.CanBeCalibrated))
            {
                _reserveQueue.Add(new Request<QuotaKeeperRequest, string>(QuotaKeeperRequest.Calibrate()));
            }
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            base.DisposeCore();

            _reserveQueue.Dispose();
            _contentItemEvicted.Dispose();
            _taskTracker.Dispose();
        }

        /// <inheritdoc />
        public override async Task SyncAsync(Context context, bool purge)
        {
            // Ensure there are no pending requests.
            var request = new Request<QuotaKeeperRequest, string>(purge ? QuotaKeeperRequest.Reserve(0) :QuotaKeeperRequest.Synchronize());
            _reserveQueue.Add(request);
            await request.WaitForCompleteAsync();

            var purgeTask = _purgeTask;
            if (purgeTask != null)
            {
                // Can ignore the result because purgeTask should never fail.
                (await purgeTask).ThrowIfFailure();
            }
        }

        private void StartPurging()
        {
            var emptyRequest = new Request<QuotaKeeperRequest, string>(QuotaKeeperRequest.Reserve(0));
            _reserveQueue.Add(emptyRequest);
            _taskTracker.Add(() => emptyRequest.WaitForCompleteAsync());
        }

        /// <inheritdoc />
        public override async Task<ReserveTransaction> ReserveAsync(long contentSize)
        {
            var reserveRequest = new Request<QuotaKeeperRequest, string>(QuotaKeeperRequest.Reserve(contentSize));
            _reserveQueue.Add(reserveRequest);

            string result = await reserveRequest.WaitForCompleteAsync();
            if (result != null)
            {
                throw new CacheException($"Failed to reserve space for content size=[{contentSize}], result=[{result}]");
            }

            return new ReserveTransaction(contentSize, OnContentEvicted);
        }

        /// <inheritdoc />
        public override void OnContentEvicted(long size)
        {
            Interlocked.Add(ref _size, -1 * size);

            if (!DisposedOrShutdownStarted)
            {
                _contentItemEvicted.Release();
            }
        }

        /// <inheritdoc />
        protected internal override Task<EvictResult> EvictContentAsync(Context context, ContentHashWithLastAccessTimeAndReplicaCount contentHashInfo, bool onlyUnlinked)
        {
            return _store.EvictAsync(context, contentHashInfo, onlyUnlinked, OnContentEvicted);
        }

        private Task CalibrateAsync(Context context, IQuotaRule rule)
        {
            var operationContext = new OperationContext(context, _token);
            return CalibrateCall.RunAsync(_tracer, operationContext, async () =>
            {
                if (!rule.CanBeCalibrated)
                {
                    return CalibrateResult.CannotCalibrate;
                }

                if (_token.IsCancellationRequested)
                {
                    var reason = "Calibrate exiting due to shutdown.";
                    _tracer.Debug(context, reason);
                    return new CalibrateResult(reason);
                }

                var result = await rule.CalibrateAsync();
                _tracer.Debug(context, !result.Succeeded ? result.ErrorMessage : $"New hard limit=[{result.Size}]");

                return result;
            });
        }

        private Task<PurgeResult> Purge(Context context, long reserveSize)
        {
            return PurgeCall.RunAsync(_tracer, new OperationContext(context), async () =>
            {
                var result = new PurgeResult();
                try
                {
                    if (_token.IsCancellationRequested)
                    {
                        _tracer.Debug(context, "Purge exiting due to shutdown.");
                        return result;
                    }

                    var purgableBytes = CurrentSize;
                    if (_usePreviousPinnedBytes)
                    {
                        purgableBytes -= _previousPinnedBytes;
                    }
                    else
                    {
                        _previousPinnedBytes = 0;
                        _usePreviousPinnedBytes = true;
                    }

                    var contentHashesWithLastAccessTime = await _store.GetLruOrderedContentListWithTimeAsync();
                    foreach (var rule in _rules)
                    {
                        if (_token.IsCancellationRequested)
                        {
                            _tracer.Debug(context, "Purge exiting due to shutdown.");
                            return result;
                        }

                        var r = await rule.PurgeAsync(context, reserveSize, contentHashesWithLastAccessTime, _token);
                        if (!r.Succeeded)
                        {
                            r.Merge(result);
                            return r;
                        }

                        _previousPinnedBytes = Math.Max(_previousPinnedBytes, r.PinnedSize);

                        result.Merge(r);
                    }
                }
                catch (Exception exception)
                {
                    _tracer.Warning(context, $"Purge threw exception: {exception}");
                }

                result.CurrentContentSize = CurrentSize;
                return result;
            });
        }

        // TODO: Purging logic leads to removing an extra file. This is due to a race condition in updating the current size. (bug 1365340)
        [SuppressMessage("AsyncUsage", "AsyncFixer05:ImplicitGenericTaskCasting")]
        private async Task Reserve(Context context)
        {
            context.Debug($"Starting purge loop. Current Size={CurrentSize}");

            // GetConsumingEnumerable means that this loop will block whenever the ReserveQueue is empty,
            // and then the loop will complete once ReserveQueue.CompleteAdding() is called and the
            // ReserveQueue empties.
            foreach (var request in _reserveQueue.GetConsumingEnumerable())
            {
                try
                {
                    if (request.Value.IsCalibrateRequest)
                    {
                        foreach (var rule in _rules.Where(r => r.CanBeCalibrated))
                        {
                            await CalibrateAsync(context, rule);
                        }

                        continue;
                    }

                    Contract.Assert(request.Value.IsReserveRequest);

                    var reserveSize = request.Value.Size;
                    var reserved = false;
                    long requestIdStartedPurge = 0;
                    var failIfDoesNotFit = false;
                    PurgeResult purgeResult = null;

                    Action reserve = () =>
                    {
                        Interlocked.Add(ref _size, reserveSize);
                        reserved = true;
                        reserveSize = 0;
                    };

                    do
                    {
                        // Shutdown started while a request call was ongoing, so we'll throw for the requesting caller.
                        if (_token.IsCancellationRequested)
                        {
                            const string message = "Reserve exiting due to shutdown";
                            _tracer.Debug(context, message);
                            request.Complete(message);
                            break;
                        }

                        // If space is immediately available under the hard limit, reserve it and complete the request.
                        var rulesNotInsideHardLimit = _rules.Where(rule => !rule.IsInsideHardLimit(reserveSize).Succeeded).ToList();

                        if (rulesNotInsideHardLimit.Count == 0)
                        {
                            reserve();
                        }
                        else if (failIfDoesNotFit)
                        {
                            // Purge task has finished here.
                            Contract.Assert(_purgeTask == null);

                            var rulesCannotBeCalibratedResults =
                                rulesNotInsideHardLimit.Where(rule => !rule.CanBeCalibrated)
                                    .Select(rule => rule.IsInsideHardLimit(reserveSize))
                                    .ToList();

                            if (rulesCannotBeCalibratedResults.Any())
                            {
                                // Some rule has reached its hard limit, and its quota cannot be calibrated.
                                var sb = new StringBuilder();

                                sb.AppendLine("Error: Failed to make space.");
                                foreach (var ruleResult in rulesCannotBeCalibratedResults)
                                {
                                    sb.AppendLine($"Hard limit surpassed. {ruleResult.ErrorMessage}");
                                }

                                request.Complete(sb.ToString());
                                break;
                            }

                            // All rules that reached their hard limits can be calibrated. We will disable such rules temporarily until calibration.
                            foreach (var rule in rulesNotInsideHardLimit.Where(rule => rule.CanBeCalibrated))
                            {
                                rule.IsEnabled = false;
                            }

                            reserve();
                        }

                        // Start purge if not already running and if now over the soft limit.
                        if (_purgeTask == null)
                        {
                            var softLimitResult = _rules.Select(rule => rule.IsInsideSoftLimit(reserveSize)).ToList();
                            if (!softLimitResult.All(rule => rule.Succeeded))
                            {
                                foreach (var ruleResult in softLimitResult.Where(rule => !rule.Succeeded))
                                {
                                    _tracer.Debug(context, $"Soft limit surpassed - Purge started. {ruleResult.ErrorMessage}");
                                }

                                requestIdStartedPurge = request.Id;
                                _purgeTask = Task.Run(() => Purge(context, reserveSize));
                            }
                        }

                        // Complete request if reserved. Do this only after starting the purge task just above
                        // so that any immediate Sync call does not race with starting of that task.
                        if (reserved)
                        {
                            request.Complete(null);
                        }

                        // If the current request has not yet been satisfied, wait until either the purge task completes or some
                        // content is evicted before trying again. If the purge task completes, set it to null so the next iteration
                        // of the loop will restart it.
                        if (!reserved && _purgeTask ==
                            await Task.WhenAny(_purgeTask, _contentItemEvicted.WaitAsync()))
                        {
                            try
                            {
                                purgeResult = await _purgeTask;

                                // If the request that initiated this completed purge is still waiting, then it was not unblocked
                                // by any intermediate contentItemEvicted events. Cause request to fail if next check for fit
                                // fails. Otherwise, the loop will run again.
                                if (request.Id == requestIdStartedPurge)
                                {
                                    failIfDoesNotFit = true;
                                }
                            }
                            finally
                            {
                                _purgeTask = null;
                            }
                        }
                    }
                    while (!reserved && (purgeResult == null || purgeResult.Succeeded || _rules.Any(r => r.CanBeCalibrated)));

                    if (!reserved && purgeResult != null && !purgeResult.Succeeded)
                    {
                        request.Complete(purgeResult.ErrorMessage);
                    }
                }
                catch (Exception e)
                {
                    // Any unexpected exception (usually from the purge task) means reservation failed.
                    var message = $"Reservation failed for size=[{request.Value}]: {e}";
                    _tracer.Warning(context, message);
                    request.Complete(message);
                }
            }
        }

        /// <summary>
        ///     Structure for request to quota keeper.
        /// </summary>
        private readonly struct QuotaKeeperRequest
        {
            /// <summary>
            ///     Gets size of reserve request.
            /// </summary>
            public long Size { get; }

            /// <summary>
            ///     Gets a value indicating whether the request is reserve request.
            /// </summary>
            public bool IsReserveRequest => _kind == RequestKind.Reserve;

            /// <summary>
            ///     Gets a value indicating whether the request is calibrate request.
            /// </summary>
            public bool IsCalibrateRequest => _kind == RequestKind.Calibrate;

            private readonly RequestKind _kind;

            /// <summary>
            ///     Initializes a new instance of the <see cref="QuotaKeeperRequest"/> struct.
            /// </summary>
            private QuotaKeeperRequest(long size, RequestKind kind)
            {
                Size = size;
                _kind = kind;
            }

            /// <summary>
            ///     Creates a reserve request.
            /// </summary>
            public static QuotaKeeperRequest Reserve(long size)
            {
                return new QuotaKeeperRequest(size, RequestKind.Reserve);
            }

            /// <summary>
            ///     Creates a calibrate request.
            /// </summary>
            public static QuotaKeeperRequest Calibrate()
            {
                return new QuotaKeeperRequest(0, RequestKind.Calibrate);
            }

            /// <summary>
            ///     Creates a synchronization request.
            /// </summary>
            public static QuotaKeeperRequest Synchronize()
            {
                return new QuotaKeeperRequest(0, RequestKind.Sync);
            }

            /// <summary>
            ///     Kinds of request to quota keeper.
            /// </summary>
            private enum RequestKind : byte
            {
                /// <summary>
                ///     Reserve request.
                /// </summary>
                Reserve,

                /// <summary>
                ///     Calibrate request.
                /// </summary>
                Calibrate,

                /// <summary>
                ///     Request used by <see cref="QuotaKeeper.SyncAsync"/>.
                /// </summary>
                Sync
            }
        }
    }
}
