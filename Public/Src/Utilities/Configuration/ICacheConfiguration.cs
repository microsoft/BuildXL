// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Cache configuration
    /// </summary>
    public partial interface ICacheConfiguration
    {
        /// <summary>
        /// Path to the cache config file.
        /// </summary>
        // TODO: Add Option for common config options for cache as literal object
        AbsolutePath CacheConfigFile { get; }

        /// <summary>
        /// Path to the cache log
        /// </summary>
        AbsolutePath CacheLogFilePath { get; }

        /// <summary>
        /// When enabled, artifacts are built incrementally based on which source files have changed. Defaults to on.
        /// </summary>
        bool Incremental { get; }

        /// <summary>
        /// Caches the build graph between runs, avoiding the parse and evaluation phases when no graph inputs have changed. Defaults to on.
        /// </summary>
        bool CacheGraph { get; }

        /// <summary>
        /// Path to graph to load
        /// </summary>
        AbsolutePath CachedGraphPathToLoad { get; }

        /// <summary>
        /// Id of graph to load
        /// </summary>
        string CachedGraphIdToLoad { get; }

        /// <summary>
        /// Whether to use the last built graph
        /// </summary>
        bool CachedGraphLastBuildLoad { get; }

        /// <summary>
        /// Caches build specification files to a single large file to improve read performance on spinning disks. When unset the behavior is dynamic based on whether root configuration file
        /// is on a drive that is detected to have a seek penalty. May be forced on or off using the flag.
        /// </summary>
        SpecCachingOption CacheSpecs { get; }

        /// <summary>
        /// How to balance memory usage vs. fetching values on demand from disk, which carries a seek penalty on spinning disks.
        /// The default automatic mode checks if the relevant drive has a seek penalty. A particular behavior may be forced using the flag.
        /// </summary>
        MemoryUsageOption CacheMemoryUsage { get; }

        /// <summary>
        /// The user defined cache session name to use for this build - optional and defaults to nothing
        /// </summary>
        string CacheSessionName { get; }

        /// <summary>
        /// Sets a rate for artificial cache misses (pips may be re-run with this likelihood, when otherwise not necessary). Miss rate and options are specified as "[~]Rate[#Seed]". The '~'
        /// symbol negates the rate (it becomes an allowed hit rate). The 'Rate' must be a numeric
        /// value in the range [0.0, 1.0]. The optional 'Seed' is an integer value fully determining the random aspect of the miss rate (the same seed and miss rate will always pick the same
        /// set of pips).
        /// </summary>
        IArtificialCacheMissConfig ArtificialCacheMissOptions { get; }

        /// <summary>
        /// An optional string to add to pip fingerprints; thus creating a separate cache universe.
        /// </summary>
        string CacheSalt { get; }

        /// <summary>
        /// When enabled, cache updates are "ignored" and cache collisions are logged in order to identify nondeterministic tools. Defaults to off.
        /// </summary>
        bool DeterminismProbe { get; }

        /// <summary>
        /// When enabled, metadata and pathsets are stored in a single file. Defaults to off.
        /// </summary>
        bool? HistoricMetadataCache { get; }

        /// <summary>
        /// The number of replicas required to guarantee that content is available. Defaults to 3. Max is 32. Requires <see cref="HistoricMetadataCache"/>=true.
        /// </summary>
        byte MinimumReplicaCountForStrongGuarantee { get; }

        /// <summary>
        /// The probability [0..100] under which content is assumed to be available when replica count >= <see cref="MinimumReplicaCountForStrongGuarantee"/>. Default is 0. Requires <see cref="HistoricMetadataCache"/>=true.
        /// </summary>
        double StrongContentGuaranteeRefreshProbability { get; }

        /// <summary>
        /// The time to to live (number of build iterations) for entries to be retained without use before eviction
        /// </summary>
        byte? FileContentTableEntryTimeToLive { get; set; }

        /// <summary>
        /// Allows for fetching cached graph from content cache.
        /// </summary>
        bool AllowFetchingCachedGraphFromContentCache { get; }

        /// <summary>
        /// Gets set of excluded paths for file change tracking
        /// </summary>
        IReadOnlyList<AbsolutePath> FileChangeTrackingExclusionRoots { get; }

        /// <summary>
        /// Gets set of included paths for file change tracking
        /// NOTE: Having at least one inclusion root forces all paths which are not under inclusion roots to be excluded
        /// </summary>
        IReadOnlyList<AbsolutePath> FileChangeTrackingInclusionRoots { get; }

        /// <summary>
        /// When enabled, the remote cache uses DedupStore instead of BlobStore.
        /// </summary>
        bool UseDedupStore { get; }

        /// <summary>
        /// When enabled, the cache will be responsible for replacing exisiting file during file materialization.
        /// </summary>
        bool ReplaceExistingFileOnMaterialization { get; }
    }
}
