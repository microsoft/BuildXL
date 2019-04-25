// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Distributed.Sessions
{
    /// <summary>
    /// A tracer for the distributed cache session.
    /// </summary>
    public class DistributedCacheSessionTracer : MemoizationStoreTracer, IMetadataCacheTracer
    {
        private const string GetSelectorsCallName = "GetSelectorsDistributed";
        private const string AddSelectorsCallName = "AddSelectorsDistributed";
        private const string InvalidateCacheEntryCallName = "InvalidateCacheEntryDistributed";
        private const string GetContentHashListCallName = "GetContentHashListDistributed";
        private const string AddContentHashListCallName = "AddContentHashListDistributed";

        private const string GetContentHashListFetchedDistributedName = "GetContentHashListFetchedDistributed";
        private const string GetContentHashListFetchedBackingName = "GetContentHashListFetchedBacking";
        private const string GetSelectorsFetchedDistributedName = "GetSelectorsFetchedDistributed";
        private const string GetSelectorsFetchedBackingName = "GetSelectorsFetchedBacking";

        private readonly CallCounter _getSelectorsCallCounter;
        private readonly CallCounter _addSelectorsCallCounter;
        private readonly CallCounter _invalidateCacheEntryCallCounter;
        private readonly CallCounter _getContentHashListCallCounter;
        private readonly CallCounter _addContentHashListCallCounter;

        private readonly Counter _getContentHashListFetchedDistributed;
        private readonly Counter _getContentHashListFetchedBacking;
        private readonly Counter _getSelectorsFetchedDistributed;
        private readonly Counter _getSelectorsFetchedBacking;

        /// <summary>
        /// Initializes a new instance of the <see cref="DistributedCacheSessionTracer" /> class.
        /// </summary>
        public DistributedCacheSessionTracer(ILogger logger, string name)
            : base(logger, name)
        {
            CallCounters.Add(_getSelectorsCallCounter = new CallCounter(GetSelectorsCallName));
            CallCounters.Add(_addSelectorsCallCounter = new CallCounter(AddSelectorsCallName));
            CallCounters.Add(_invalidateCacheEntryCallCounter = new CallCounter(InvalidateCacheEntryCallName));
            CallCounters.Add(_getContentHashListCallCounter = new CallCounter(GetContentHashListCallName));
            CallCounters.Add(_addContentHashListCallCounter = new CallCounter(AddContentHashListCallName));

            Counters.Add(_getContentHashListFetchedDistributed = new Counter(GetContentHashListFetchedDistributedName));
            Counters.Add(_getContentHashListFetchedBacking = new Counter(GetContentHashListFetchedBackingName));
            Counters.Add(_getSelectorsFetchedDistributed = new Counter(GetSelectorsFetchedDistributedName));
            Counters.Add(_getSelectorsFetchedBacking = new Counter(GetSelectorsFetchedBackingName));
        }

        /// <inheritdoc />
        public void GetDistributedSelectorsStart(Context context)
        {
            _getSelectorsCallCounter.Started();
        }

        /// <inheritdoc />
        public void GetDistributedSelectorsStop(Context context, TimeSpan elapsed)
        {
            _getSelectorsCallCounter.Completed(elapsed.Ticks);
        }

        /// <inheritdoc />
        public void AddSelectorsStart(Context context)
        {
            _addSelectorsCallCounter.Started();
        }

        /// <inheritdoc />
        public void AddSelectorsStop(Context context, TimeSpan elapsed)
        {
            _addSelectorsCallCounter.Completed(elapsed.Ticks);
        }

        /// <inheritdoc />
        public void InvalidateCacheEntryStart(Context context, StrongFingerprint strongFingerprint)
        {
            Debug(context, $"Deleting redis keys for {strongFingerprint} & {strongFingerprint.WeakFingerprint}.");
            _invalidateCacheEntryCallCounter.Started();
        }

        /// <inheritdoc />
        public void InvalidateCacheEntryStop(Context context, TimeSpan elapsed)
        {
            _invalidateCacheEntryCallCounter.Completed(elapsed.Ticks);
        }

        /// <inheritdoc />
        public void GetContentHashListStart(Context context)
        {
            _getContentHashListCallCounter.Started();
        }

        /// <inheritdoc />
        public void GetContentHashListStop(Context context, TimeSpan elapsed)
        {
            _getContentHashListCallCounter.Completed(elapsed.Ticks);
        }

        /// <inheritdoc />
        public void AddContentHashListStart(Context context)
        {
            _addContentHashListCallCounter.Started();
        }

        /// <inheritdoc />
        public void AddContentHashListStop(Context context, TimeSpan elapsed)
        {
            _addContentHashListCallCounter.Completed(elapsed.Ticks);
        }

        /// <inheritdoc />
        public void RecordGetContentHashListFetchedDistributed(Context context, StrongFingerprint strongFingerprint)
        {
            Debug(context, $"Redis cache hit for {strongFingerprint}");
            _getContentHashListFetchedDistributed.Increment();
        }

        /// <inheritdoc />
        public void RecordContentHashListFetchedFromBackingStore(Context context, StrongFingerprint strongFingerprint)
        {
            Debug(context, $"Redis cache miss for {strongFingerprint}.");
            _getContentHashListFetchedBacking.Increment();
        }

        /// <inheritdoc />
        public void RecordSelectorsFetchedFromBackingStore(Context context, Fingerprint weakFingerprint)
        {
            Debug(context, $"Redis cache miss for {weakFingerprint}.");
            _getSelectorsFetchedBacking.Increment();
        }

        /// <inheritdoc />
        public void RecordSelectorsFetchedDistributed(Context context, Fingerprint weakFingerprint, int selectorsCount)
        {
            Debug(context, $"Redis cache hit for {weakFingerprint}. Found {selectorsCount} selectors.");
            _getSelectorsFetchedDistributed.Increment();
        }
    }
}
