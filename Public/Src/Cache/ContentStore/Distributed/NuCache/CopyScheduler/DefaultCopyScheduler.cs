// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    /// <summary>
    /// The default scheduler follows our usual way to schedule copies: there's two separate semaphores (one for
    /// push copies, one for pull copies), and copies will await into one of them depending on what they are.
    /// </summary>
    public class DefaultCopyScheduler : ICopyScheduler
    {
        private readonly DefaultCopySchedulerConfiguration _configuration;

        /// <summary>
        /// Gate to control the maximum number of simultaneously active IO operations.
        /// </summary>
        private readonly OrderedSemaphore _outboundPullGate;

        /// <summary>
        /// Gate to control the maximum number of simultaneously active proactive copies.
        /// </summary>
        private readonly OrderedSemaphore _outboundPushGate;

        /// <nodoc />
        public DefaultCopyScheduler(DefaultCopySchedulerConfiguration configuration, Context tracingContext)
        {
            _configuration = configuration;

            _outboundPullGate = new OrderedSemaphore(
                configuration.OutboundPullConfiguration.MaximumConcurrency,
                configuration.OutboundPullConfiguration.SemaphoreOrder,
                tracingContext);

            _outboundPushGate = new OrderedSemaphore(
                configuration.OutboundPushConfiguration.MaximumConcurrency,
                configuration.OutboundPushConfiguration.SemaphoreOrder,
                tracingContext);
        }

        /// <inheritdoc />
        public Task<CopySchedulerResult<CopyFileResult>> ScheduleOutboundPullAsync(OutboundPullCopy request)
        {
            return _outboundPullGate.GatedOperationAsync(async pair =>
                {
                    var (timeWaiting, currentCount) = pair;

                    var queueLength = _outboundPullGate.ConcurrencyLimit - currentCount;
                    var summary = new CopySchedulingSummary(
                        QueueWait: timeWaiting,
                        PriorityQueueLength: queueLength,
                        Priority: 0,
                        OverallQueueLength: queueLength);

                    return new CopySchedulerResult<CopyFileResult>(await request.PerformOperationAsync(new OutboundPullArguments(request.Context, summary)));
                },
                request.Context.Token,
                _configuration.OutboundPullConfiguration.WaitTimeout,
                onTimeout: _ => CopySchedulerResult<CopyFileResult>.TimeOut());
        }

        /// <inheritdoc />
        public Task<CopySchedulerResult<T>> ScheduleOutboundPushAsync<T>(OutboundPushCopy<T> request)
            where T : class
        {
            return _outboundPushGate.GatedOperationAsync(async pair =>
                {
                    var (timeWaiting, currentCount) = pair;

                    var queueLength = _outboundPushGate.ConcurrencyLimit - currentCount;
                    var summary = new CopySchedulingSummary(
                        QueueWait: timeWaiting,
                        PriorityQueueLength: queueLength,
                        Priority: 0,
                        OverallQueueLength: queueLength);

                    return new CopySchedulerResult<T>(await request.PerformOperationAsync(new OutboundPushArguments(request.Context, summary)));
                },
                request.Context.Token,
                _configuration.OutboundPushConfiguration.WaitTimeout,
                onTimeout: _ => CopySchedulerResult<T>.TimeOut());
        }
    }
}
