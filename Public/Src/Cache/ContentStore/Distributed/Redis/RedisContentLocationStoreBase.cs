// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tracing;
using StackExchange.Redis;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// A base type responsible for interaction with <see cref="ContentLocationDatabase"/> instance.
    /// </summary>
    internal abstract class RedisContentLocationStoreBase : StartupShutdownBase
    {
        private readonly IClock _clock;

        protected readonly RedisContentLocationStoreConfiguration Configuration;
        protected MachineId? LocalMachineId;

        protected NagleQueue<ContentHashWithSize> TouchNagleQueue;

        /// <nodoc />
        public CounterCollection<ContentLocationStoreCounters> Counters { get; } = new CounterCollection<ContentLocationStoreCounters>();

        /// <inheritdoc />
        protected RedisContentLocationStoreBase(IClock clock, RedisContentLocationStoreConfiguration configuration)
        {
            Contract.Requires(clock != null);

            _clock = clock;
            configuration = configuration ?? RedisContentLocationStoreConfiguration.Default;
            Configuration = configuration;
        }

        private async Task BackgroundTouchBulkAsync(OperationContext context, ContentHashWithSize[] hashes)
        {
            await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // Don't need to notify event store because it was already notified.
                    await TouchBulkInternalAsync(context.TracingContext, hashes, notifyEventStore: false, cts: CancellationToken.None);
                    return BoolResult.Success;
                },
                Counters[ContentLocationStoreCounters.BackgroundTouchBulk])
                .IgnoreFailure(); // All errors are traces and nothing we can do here
        }

        /// <nodoc />
        protected abstract Task TouchBulkInternalAsync(
            Context context,
            IReadOnlyList<ContentHashWithSize> contentHashesWithSize,
            bool notifyEventStore,
            CancellationToken cts);

        /// <nodoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            TouchNagleQueue = NagleQueue<ContentHashWithSize>.Create(
                hashes => BackgroundTouchBulkAsync(context, hashes),
                RedisContentLocationStoreConstants.BatchDegreeOfParallelism,
                RedisContentLocationStoreConstants.BatchInterval,
                RedisContentLocationStoreConstants.DefaultBatchSize);

            return BoolResult.SuccessTask;
        }

        /// <nodoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            TouchNagleQueue?.Dispose();
            return BoolResult.SuccessTask;
        }

        /// <nodoc />
        public Task<BoolResult> RegisterLocalLocationAsync(Context context, IReadOnlyList<ContentHashWithSize> contentHashes, CancellationToken cts, UrgencyHint urgencyHint, bool touch = true)
        {
            var operationContext = new OperationContext(context, cts);
            return operationContext.PerformOperationAsync(
                Tracer,
                () => RegisterLocalLocationWithCentralStoreAsync(context, contentHashes, cts, urgencyHint),
                Counters[ContentLocationStoreCounters.RegisterLocalLocation]);
        }

        /// <nodoc />
        protected abstract Task<MachineId> GetLocalLocationIdAsync(Context context, CancellationToken cts);

        /// <nodoc />
        protected abstract Task<BoolResult> RegisterLocalLocationWithCentralStoreAsync(
            Context context,
            IReadOnlyList<ContentHashWithSize> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint);

        /// <nodoc />
        protected ContentLocationEntry ToContentLocationEntry(RedisValue contentHashInfo)
        {
            return ContentLocationEntry.FromRedisValue(contentHashInfo, _clock.UtcNow);
        }

        /// <summary>
        /// Gets the counters with high level statistics associated with a current instance.
        /// </summary>
        public virtual CounterSet GetCounters(Context context)
        {
            return Counters.ToCounterSet();
        }
    }
}
