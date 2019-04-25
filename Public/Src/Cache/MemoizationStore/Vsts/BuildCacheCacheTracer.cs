// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    /// A tracer wrapper for the BuildCacheCache layer.
    /// </summary>
    public sealed class BuildCacheCacheTracer : CacheTracer
    {
        private const string PrefetchedContentHashListCountName = "PrefetchedContentHashList";
        private const string PrefetchedContentHashListUsedCountName = "PrefetchedContentHashListUsed";

        /// <summary>
        /// Tracer for contentsession related activities.
        /// </summary>
        public readonly BackingContentStoreTracer ContentSessionTracer;

        /// <summary>
        /// Tracer for memoization related activities.
        /// </summary>
        public readonly MemoizationStoreTracer MemoizationStoreTracer;
        private readonly Counter _prefetchedContentHashListCounter;
        private readonly Counter _prefetchedContentHashListUsedCounter;
        private readonly List<Counter> _counters = new List<Counter>();

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildCacheCacheTracer"/> class.
        /// </summary>
        public BuildCacheCacheTracer(ILogger logger, string name)
            : base(name)
        {
            ContentSessionTracer = new BackingContentStoreTracer(name);
            MemoizationStoreTracer = new MemoizationStoreTracer(logger, name);

            _counters.Add(_prefetchedContentHashListCounter = new Counter(PrefetchedContentHashListCountName));
            _counters.Add(_prefetchedContentHashListUsedCounter = new Counter(PrefetchedContentHashListUsedCountName));
        }

        /// <summary>
        /// Gets the counters collected by this tracer instance.
        /// </summary>
        public CounterSet GetCounters()
        {
            var aggregatedCounters = new CounterSet();
            aggregatedCounters.Merge(ContentSessionTracer.GetCounters());
            var countersStored = aggregatedCounters.ToDictionaryIntegral();
            foreach (var counter in MemoizationStoreTracer.GetCounters().ToDictionaryIntegral())
            {
                if (!countersStored.ContainsKey(counter.Key))
                {
                    aggregatedCounters.Add(counter.Key, counter.Value);
                }
            }

            foreach (Counter counter in _counters)
            {
                aggregatedCounters.Add(counter.Name, counter.Value);
            }

            return aggregatedCounters;
        }

        /// <summary>
        /// Records that a content hashlist was prefetched as part of the selectors call.
        /// </summary>
        public void RecordPrefetchedContentHashList()
        {
            _prefetchedContentHashListCounter.Increment();
        }

        /// <summary>
        /// Records that a content hash list was used from the prefetched cache.
        /// </summary>
        public void RecordUseOfPrefetchedContentHashList()
        {
            _prefetchedContentHashListUsedCounter.Increment();
        }
    }
}
