// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Wrapper for functions associated with distributed pinning. Used as a flag.
    /// </summary>
    public class DistributedEvictionSettings
    {
        /// <summary>
        /// Default fudge factor for replicas.
        /// </summary>
        public const int DefaultReplicaCreditInMinutes = 180;

        /// <summary>
        /// Distributed store used in a next-gen distributed eviction logic based on a local location store.
        /// </summary>
        public IDistributedLocationStore DistributedStore;

        /// <summary>
        /// Function to retrieve last access time from content tracker.
        /// </summary>
        public readonly TrimOrGetLastAccessTimeAsync TrimOrGetLastAccessTimeAsync;

        /// <summary>
        /// Function to update content directory with content tracker's last access time.
        /// </summary>
        public UpdateContentWithLastAccessTimeAsync UpdateContentWithLastAccessTimeAsync;

        /// <summary>
        /// Location store batch size for eviction, piped from location store config.
        /// </summary>
        public readonly int LocationStoreBatchSize;

        /// <summary>
        /// Nagle queue for re-adding hashes to the content tracker after being considered for eviction.
        /// </summary>
        public NagleQueue<ContentHash> ReregisterHashQueue;

        /// <summary>
        /// Tracer.
        /// </summary>
        public Tracer Tracer;

        /// <summary>
        /// Function to check if content is locally pinned and returns its size.
        /// </summary>
        public PinnedSizeChecker PinnedSizeChecker;

        /// <summary>
        /// Fudge factor for replica count. Toggles the use of the final version of distributed eviction.
        /// </summary>
        public readonly int ReplicaCreditInMinutes;

        /// <summary>
        /// Whether or not Distributed Eviction was successfully set up.
        /// </summary>
        public bool IsInitialized;

        /// <summary>
        /// Initializes a new instance of the <see cref="DistributedEvictionSettings"/> class.
        /// </summary>
        public DistributedEvictionSettings(
            TrimOrGetLastAccessTimeAsync trimOrGetLastAccessTimeAsync,
            int locationStoreBatchSize,
            int? replicaCreditInMinutes,
            IDistributedLocationStore distributedStore)
        {
            Contract.Assert(trimOrGetLastAccessTimeAsync != null);

            TrimOrGetLastAccessTimeAsync = trimOrGetLastAccessTimeAsync;
            LocationStoreBatchSize = locationStoreBatchSize;
            ReplicaCreditInMinutes = replicaCreditInMinutes ?? DefaultReplicaCreditInMinutes;
            IsInitialized = false;
            DistributedStore = distributedStore;
        }

        /// <summary>
        /// Finish setting up distributed eviction.
        /// </summary>
        public void InitializeDistributedEviction(
            UpdateContentWithLastAccessTimeAsync updateMetadataFunc,
            Tracer tracer,
            PinnedSizeChecker pinnedSizeChecker,
            NagleQueue<ContentHash> reregisterHashQueue)
        {
            Contract.Assert(updateMetadataFunc != null);
            Contract.Assert(pinnedSizeChecker != null);
            Contract.Assert(tracer != null);
            Contract.Assert(reregisterHashQueue != null);

            UpdateContentWithLastAccessTimeAsync = updateMetadataFunc;
            Tracer = tracer;
            PinnedSizeChecker = pinnedSizeChecker;
            ReregisterHashQueue = reregisterHashQueue;
            IsInitialized = true;
        }
    }
}
