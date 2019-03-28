// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// A tracer wrapper for the BuildCacheCache layer.
    /// </summary>
    public sealed class BackingContentStoreTracer : ContentSessionTracer
    {
        private const string PinSatisfiedFromRemote = "VstsPinSatisfiedRemoteCount";
        private const string PinSatisfiedInMemory = "VstsPinSatisfiedInMemoryCount";
        private const string VstsDownloadUriFetchedFromRemote = "VstsDownloadUriFetchedRemoteCount";
        private const string VstsDownloadUriFetchedInMemory = "VstsDownloadUriFetchedInMemoryCount";

        /// <summary>
        /// Tracer for memoization related activities.
        /// </summary>
        private readonly Counter _pinCallsSatisfiedInMemory;
        private readonly Counter _pinCallsSatisfiedRemote;
        private readonly Counter _downloadUriFetchedRemote;
        private readonly Counter _downloadUriFetchedInMemory;
        private readonly List<Counter> _counters = new List<Counter>();

        /// <summary>
        /// Initializes a new instance of the <see cref="BackingContentStoreTracer"/> class.
        /// </summary>
        public BackingContentStoreTracer(string name, string category = null)
            : base(name)
        {
            _counters.Add(_pinCallsSatisfiedInMemory = new Counter(PinSatisfiedInMemory));
            _counters.Add(_pinCallsSatisfiedRemote = new Counter(PinSatisfiedFromRemote));
            _counters.Add(_downloadUriFetchedRemote = new Counter(VstsDownloadUriFetchedFromRemote));
            _counters.Add(_downloadUriFetchedInMemory = new Counter(VstsDownloadUriFetchedInMemory));
        }

        /// <summary>
        /// Gets the counters collected by this tracer instance.
        /// </summary>
        public override CounterSet GetCounters()
        {
            var aggregatedCounters = base.GetCounters();

            foreach (var counter in _counters)
            {
                aggregatedCounters.Add(counter.Name, counter.Value);
            }

            return aggregatedCounters;
        }

        /// <summary>
        /// Records that a Pin was satisfied without reaching VSTS based on existing cached data.
        /// </summary>
        public void RecordPinSatisfiedInMemory()
        {
            _pinCallsSatisfiedInMemory.Increment();
        }

        /// <summary>
        /// Records that a Pin request had to be made to a remote VSTS store.
        /// </summary>
        public void RecordPinSatisfiedFromRemote()
        {
            _pinCallsSatisfiedRemote.Increment();
        }

        /// <summary>
        /// Records that a Download URI had to be obtained from calling a remote VSTS service.
        /// </summary>
        public void RecordDownloadUriFetchedFromRemote()
        {
            _downloadUriFetchedRemote.Increment();
        }

        /// <summary>
        ///  Records that a DownloadUri was fetched from the in-memory cache.
        /// </summary>
        public void RecordDownloadUriFetchedFromCache()
        {
            _downloadUriFetchedInMemory.Increment();
        }
    }
}
