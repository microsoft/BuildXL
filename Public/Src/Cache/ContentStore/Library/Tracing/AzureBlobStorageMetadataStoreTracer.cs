// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

using System;
using System.Collections.ObjectModel;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    /// Traces specific information related to AzureBlobStorageMetadataStore
    /// </summary>
    /// <remarks>
    /// TODO: We should remove this class and make the AzureBlobMetadataStore use a private counter set directly.
    /// </remarks>
    public class AzureBlobStorageMetadataStoreTracer : Tracer
    {
        protected const string PinnedContentWasNotFound = "PinnedContentWasNotFound";
        protected const string ContentWasPinned = "ContentWasPinned";
        
        protected readonly Collection<CallCounter> CallCounters = new Collection<CallCounter>();
        private readonly CallCounter _pinnedContentWasNotFound;
        private readonly CallCounter _contentWasPinned;
        
        /// <nodoc/>
        public AzureBlobStorageMetadataStoreTracer(string name)
            : base(name)
        {
            CallCounters.Add(_pinnedContentWasNotFound = new CallCounter(PinnedContentWasNotFound));
            CallCounters.Add(_contentWasPinned = new CallCounter(ContentWasPinned));
        }

        /// <nodoc/>
        public virtual CounterSet GetCounters()
        {
            var callsCounterSet = new CounterSet();

            foreach (var callCounter in CallCounters)
            {
                callCounter.AppendTo(callsCounterSet);
            }

            return callsCounterSet;
        }

        /// <nodoc/>
        public virtual void PinnedContentWasNotFoundStart(Context context)
        {
            _pinnedContentWasNotFound.Started();
        }

        /// <nodoc/>
        public virtual void PinnedContentWasNotFoundStop(Context context, TimeSpan elapsed)
        {
            _pinnedContentWasNotFound.Completed(elapsed.Ticks);
        }

        /// <nodoc/>
        public virtual void ContentWasPinnedStart(Context context)
        {
            _contentWasPinned.Started();
        }

        /// <nodoc/>
        public virtual void ContentWasPinnedStop(Context context, TimeSpan elapsed)
        {
            _contentWasPinned.Completed(elapsed.Ticks);
        }
    }
}
