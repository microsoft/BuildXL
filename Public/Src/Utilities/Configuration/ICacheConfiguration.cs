// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

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
        /// The user defined cache session name to use for this build - optional and defaults to nothing
        /// </summary>
        string CacheSessionName { get; }

        /// <summary>
        /// Sets a rate for artificial cache misses (pips may be re-run with this likelihood, when otherwise not necessary). Miss rate and options are specified as "[~]Rate[#Seed]". The '~'
        /// symbol negates the rate (it becomes an allowed hit rate). The 'Rate' must be a numeric
        /// value in the range [0.0, 1.0]. The optional 'Seed' is an integer value fully determining the random aspect of the miss rate (the same seed and miss rate will always pick the same
        /// set of pips).
        /// </summary>
        IArtificialCacheMissConfig ArtificialCacheMissConfig { get; }

        /// <summary>
        /// An optional string to add to pip fingerprints; thus creating a separate cache universe.
        /// </summary>
        string CacheSalt { get; }

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
        ushort? FileContentTableEntryTimeToLive { get; set; }

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
        /// Indicates whether minimal graph enumerations should elide absent path probes in the directory root
        /// </summary>
        bool ElideMinimalGraphEnumerationAbsentPathProbes { get; }

        /// <summary>
        /// The maximum number of visited path sets allowed before switching to an 'augmented' weak fingerprint
        /// computed from common dynamically accessed paths.
        /// </summary>
        int AugmentWeakFingerprintPathSetThreshold { get; }

        /// <summary>
        /// Used to compute the number of times (i.e. <see cref="AugmentWeakFingerprintRequiredPathCommonalityFactor"/> * <see cref="AugmentWeakFingerprintPathSetThreshold"/>) an entry must
        /// appear among paths in the observed path set in order to be included in the common path set. Value must be (0, 1]
        /// </summary>
        double AugmentWeakFingerprintRequiredPathCommonalityFactor { get; }

        /// <summary>
        /// Controls the max number of logged suspicious paths (i.e., paths used in an augmented pathset, but not observed during pip execution) for each pip. 
        /// The value of 0 means that the monitoring is disabled.
        /// </summary>
        int MonitorAugmentedPathSets { get; }

        /// <summary>
        /// The maximum number of path sets to be checked during the cache lookup.
        /// </summary>
        /// <remarks>
        /// Value less than or equal to 0 means that there is no limit.
        /// Enabling this feature, i.e., setting the value to be larger than 0, can prevent the build from checking too many
        /// path sets due to path set explosion. However, it can also cause the build to have more cache misses.
        /// </remarks>
        int MaxPathSetsOnCacheLookup { get; }

        /// <summary>
        /// When enabled, the cache will be responsible for replacing exisiting file during file materialization.
        /// </summary>
        bool ReplaceExistingFileOnMaterialization { get; }

        /// <summary>
        /// Path to the content addressable store used by the BuildXL virtual file system process.
        /// </summary>
        /// <remarks>
        /// For virtualized files, symlinks are placed in target location which point to the content addressabe files under
        /// this path.
        /// </remarks>
        AbsolutePath VfsCasRoot { get; }

        /// <summary>
        /// Gets whether inputs to pips without historical file access info should be virtualized
        /// </summary>
        bool VirtualizeUnknownPips { get; }

        /// <summary>
        /// When true, only a local cache will be created, even if a remote cache is configured.
        /// </summary>
        bool? UseLocalOnly { get; }

        /// <summary>
        /// When true, casing of directories of the produced outputs will match the one found on disk (as opposed to using the casing of the first time the path is mentioned in the build)
        /// </summary>
        bool HonorDirectoryCasingOnDisk { get; }

        /// <summary>
        /// For source files, allow reusing file's weak identity when recording a new content hash.
        /// </summary>
        bool AllowReuseOfWeakIdenityForSourceFiles { get; }

        /// <summary>
        /// Whether to send cache log events to Kusto.
        /// </summary>
        /// <remarks>
        /// False by default. This is a temporary flag to control the deployment of the cache log Kusto upload feature feature.
        /// TODO: This should be removed once the feature has been validated, and <see cref="ILoggingConfiguration.LogToKusto"/> should drive that decision
        /// </remarks>
        public bool CacheLogToKusto { get; }
    }
}
