// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Cache
{
    /// <summary>
    /// Counters for <see cref="PipTwoPhaseCache" />.
    /// Primarily one would retrieve these counters from <see cref="PipTwoPhaseCache.Counters"/>.
    /// </summary>
    public enum PipCachingCounter
    {
        /// <summary>
        /// The total size in bytes of metadata blobs stored
        /// </summary>
        StoredMetadataSize,

        /// <summary>
        /// The number of metadata blobs stored
        /// </summary>
        StoredMetadataCount,

        /// <summary>
        /// The total size in bytes of metadata blobs retrieved
        /// </summary>
        LoadedMetadataSize,

        /// <summary>
        /// The number of metadata blobs retrieved
        /// </summary>
        LoadedMetadataCount,

        /// <summary>
        /// The total size in bytes of path set blobs stored
        /// </summary>
        StoredPathSetSize,

        /// <summary>
        /// The number of path set blobs stored
        /// </summary>
        StoredPathSetCount,

        /// <summary>
        /// The total size in bytes of path set blobs retrieved
        /// </summary>
        LoadedPathSetSize,

        /// <summary>
        /// The number of path set blobs retrieved
        /// </summary>
        LoadedPathSetCount,

        /// <summary>
        /// How many times the metadata retrieval is failed.
        /// </summary>
        MetadataRetrievalFails,

        /// <summary>
        /// How many times the weak fingerprint fetched from cache is not useful.
        /// </summary>
        WeakFingerprintMisses,

        /// <summary>
        /// The loading duration for historic metadata cache file from cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HistoricRetrievalDuration,

        /// <summary>
        /// The deserialization duration for historic metadata cache file from cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HistoricDeserializationDuration,

        /// <summary>
        /// The saving duration for historic metadata cache file to cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HistoricSavingDuration,

        /// <summary>
        /// The serialization duration for historic metadata cache file from cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HistoricSerializationDuration,

        /// <summary>
        /// Whether the historic metadata cache is loaded from disk
        /// </summary>
        HistoricLoadedFromDisk,

        /// <summary>
        /// Whether the historic metadata cache is loaded from cache
        /// </summary>
        HistoricLoadedFromCache,

        /// <summary>
        /// Metadata hits from historic metadata cache
        /// </summary>
        HistoricMetadataHits,

        /// <summary>
        /// Metadata misses from historic metadata cache
        /// </summary>
        HistoricMetadataMisses,

        /// <summary>
        /// Pathset misses from historic metadata cache
        /// </summary>
        HistoricPathSetMisses,

        /// <summary>
        /// Pathset hits from historic metadata cache
        /// </summary>
        HistoricPathSetHits,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HistoricCollectDuration,

        /// <nodoc />
        HistoricCollectRemovedBlobCount,

        /// <nodoc />
        HistoricCollectTotalBlobCount,

        /// <nodoc />
        HistoricCollectCancelled,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HistoricCollectMaxBatchEvictionTime,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HistoricTryAddContentDuration,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HistoricTryGetContentDuration,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HistoricTryAddPathSetDuration,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HistoricTryAddMetadataDuration,

        /// <summary>
        /// Historic saved total size
        /// </summary>
        HistoricSavedSizeBytes,

        /// <summary>
        /// PathSet received from remote workers
        /// </summary>
        HistoricPathSetCountFromRemoteExecution,

        /// <summary>
        /// PathSet received from remote workers
        /// </summary>
        HistoricPathSetCountFromRemoteLookup,

        /// <summary>
        /// PathSet received from remote workers
        /// </summary>
        HistoricPathSetExistCountFromRemoteExecution,

        /// <summary>
        /// PathSet received from remote workers
        /// </summary>
        HistoricPathSetExistCountFromRemoteLookup,

        /// <summary>
        /// Metadata received from remote workers
        /// </summary>
        HistoricMetadataCountFromRemoteExecution,

        /// <summary>
        /// Metadata received from remote workers
        /// </summary>
        HistoricMetadataCountFromRemoteLookup,

        /// <summary>
        /// Metadata received from remote workers
        /// </summary>
        HistoricMetadataExistCountFromRemoteExecution,

        /// <summary>
        /// Metadata received from remote workers
        /// </summary>
        HistoricMetadataExistCountFromRemoteLookup,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        HistoricCacheEntrySerializationDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        HistoricCacheEntryDeserializationDuration,

        /// <nodoc/>
        HistoricWeakFingerprintHits,

        /// <nodoc/>
        HistoricWeakFingerprintMisses,

        /// <nodoc/>
        HistoricCacheEntryHits,

        /// <nodoc/>
        HistoricCacheEntryMisses,

        /// <nodoc/>
        HistoricSavedCacheEntriesSizeBytes,

        /// <nodoc/>
        HistoricWeakFingerprintSavedCount,

        /// <nodoc/>
        HistoricWeakFingerprintExpiredCount,

        /// <nodoc/>
        HistoricStrongFingerprintSavedCount,

        /// <nodoc/>
        HistoricStrongFingerprintPurgedCount,

        /// <nodoc/>
        HistoricStrongFingerprintLoadedCount,

        /// <nodoc/>
        HistoricWeakFingerprintLoadedCount,

        /// <nodoc/>
        HistoricCacheEntryCountFromRemoteExecution,

        /// <nodoc/>
        HistoricCacheEntryCountFromRemoteLookup,

        /// <nodoc/>
        HistoricCacheEntryExistCountFromRemoteExecution,

        /// <nodoc/>
        HistoricCacheEntryExistCountFromRemoteLookup,

        /// <nodoc/>
        HistoricMetadataLoadedAge,

        /// <nodoc/>
        HistoricStrongFingerprintDuplicatesCount,

        /// <nodoc/>
        HistoricStrongFingerprintInvalidCount,
    }
}
