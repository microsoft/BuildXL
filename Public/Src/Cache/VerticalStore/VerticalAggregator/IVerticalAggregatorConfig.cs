// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.VerticalAggregator
{
    internal interface IVerticalAggregatorConfig
    {
        /// <summary>
        /// Treat the remote cache as read only. Still pull from it.
        /// </summary>
        public bool RemoteIsReadOnly { get; }

        /// <summary>
        /// Start a background prefetch of CAS data from the remote CAS when a FullCacheRecord is returned.
        /// </summary>
        /// <remarks>
        /// Currently not supported.
        /// </remarks>
        public bool PreFetchCasData { get; }

        /// <summary>
        /// Write CAS data to remote and block on completion.
        /// </summary>
        public bool WriteThroughCasData { get; }

        /// <summary>
        /// If true, fail construction of the cache if the Remote cache fails
        /// </summary>
        /// <remarks>
        /// Normally, if the remote cache fails but the local cache works, the
        /// construction will just return the local cache as a basic fallback.
        /// If, however, the remote cache is considered critical, setting this to
        /// true will fail the cache construction if the remote cache is not
        /// functioning.
        /// </remarks>
        public bool FailIfRemoteFails { get; }

        /// <summary>
        /// Remote content is read-only and we should only try to put metadata into the cache.
        /// </summary>
        public bool RemoteContentIsReadOnly { get; }

        /// <summary>
        /// Create only the local cache.
        /// </summary>
        public bool UseLocalOnly { get; }

        /// <summary>
        /// Timeout for the amount of time it can take to construct the remote cache.
        /// </summary>
        public int RemoteConstructionTimeoutMilliseconds { get; }

        /// <summary>
        /// Whether to prohibit read operations on the remote cache.
        /// </summary>
        public bool RemoteIsWriteOnly { get; }

        /// <summary>
        /// Whether to skip the determinism check / recovery on cache hits.
        /// When we have a local cache hit, we still query the remote cache
        /// to avoid divergence between the local and remote cache and recover
        /// determisism. If this is true, we skip that logic and just return the local
        /// result whenever it is a hit.
        /// </summary>
        public bool SkipDeterminismRecovery { get; }
    }
}
