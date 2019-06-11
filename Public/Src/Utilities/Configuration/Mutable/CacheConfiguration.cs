// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class CacheConfiguration : ICacheConfiguration
    {
        /// <nodoc />
        public CacheConfiguration()
        {
            CacheConfigFile = FileArtifact.Invalid;
            CacheSpecs = SpecCachingOption.Auto;
            Incremental = true;
            CacheGraph = true;
            CacheSalt = null;
            CacheSessionName = string.Empty;
            DeterminismProbe = false;
            AllowFetchingCachedGraphFromContentCache = true;
            MinimumReplicaCountForStrongGuarantee = 3;
            StrongContentGuaranteeRefreshProbability = 1;
            FileChangeTrackingExclusionRoots = new List<AbsolutePath>();
            FileChangeTrackingInclusionRoots = new List<AbsolutePath>();
            ReplaceExistingFileOnMaterialization = false;
        }

        /// <nodoc />
        public CacheConfiguration(ICacheConfiguration template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            CacheConfigFile = pathRemapper.Remap(template.CacheConfigFile);
            CacheLogFilePath = pathRemapper.Remap(template.CacheLogFilePath);
            Incremental = template.Incremental;
            CacheGraph = template.CacheGraph;
            CachedGraphPathToLoad = pathRemapper.Remap(template.CachedGraphPathToLoad);
            CachedGraphIdToLoad = template.CachedGraphIdToLoad;
            CachedGraphLastBuildLoad = template.CachedGraphLastBuildLoad;
            CacheSpecs = template.CacheSpecs;
            CacheMemoryUsage = template.CacheMemoryUsage;
            CacheSessionName = template.CacheSessionName;
            ArtificialCacheMissOptions = template.ArtificialCacheMissOptions == null ? null : new ArtificialCacheMissConfig(template.ArtificialCacheMissOptions);
            CacheSalt = template.CacheSalt;
            DeterminismProbe = template.DeterminismProbe;
            HistoricMetadataCache = template.HistoricMetadataCache;
            AllowFetchingCachedGraphFromContentCache = template.AllowFetchingCachedGraphFromContentCache;
            MinimumReplicaCountForStrongGuarantee = template.MinimumReplicaCountForStrongGuarantee;
            StrongContentGuaranteeRefreshProbability = template.StrongContentGuaranteeRefreshProbability;
            FileChangeTrackingExclusionRoots = pathRemapper.Remap(template.FileChangeTrackingExclusionRoots);
            FileChangeTrackingInclusionRoots = pathRemapper.Remap(template.FileChangeTrackingInclusionRoots);
            UseDedupStore = template.UseDedupStore;
            ReplaceExistingFileOnMaterialization = template.ReplaceExistingFileOnMaterialization;
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public AbsolutePath CacheConfigFile { get; set; }

        /// <inheritdoc />
        AbsolutePath ICacheConfiguration.CacheConfigFile => CacheConfigFile;

        /// <inheritdoc />
        public AbsolutePath CacheLogFilePath { get; set; }

        /// <inheritdoc />
        public bool Incremental { get; set; }

        /// <inheritdoc />
        public byte? FileContentTableEntryTimeToLive { get; set; }

        /// <inheritdoc />
        public bool AllowFetchingCachedGraphFromContentCache { get; set; }

        /// <inheritdoc />
        public bool CacheGraph { get; set; }

        /// <inheritdoc />
        public AbsolutePath CachedGraphPathToLoad { get; set; }

        /// <inheritdoc />
        public string CachedGraphIdToLoad { get; set; }

        /// <inheritdoc />
        public bool CachedGraphLastBuildLoad { get; set; }

        /// <inheritdoc />
        public SpecCachingOption CacheSpecs { get; set; }

        /// <inheritdoc />
        public MemoryUsageOption CacheMemoryUsage { get; set; }

        /// <inheritdoc />
        public string CacheSessionName { get; set; }

        /// <nodoc />
        public ArtificialCacheMissConfig ArtificialCacheMissOptions { get; set; }

        /// <inheritdoc />
        IArtificialCacheMissConfig ICacheConfiguration.ArtificialCacheMissOptions => ArtificialCacheMissOptions;

        /// <inheritdoc />
        public string CacheSalt { get; set; }

        /// <inheritdoc />
        public bool DeterminismProbe { get; set; }

        /// <inheritdoc />
        public bool? HistoricMetadataCache { get; set; }

        /// <inheritdoc />
        public byte MinimumReplicaCountForStrongGuarantee { get; set; }

        /// <inheritdoc />
        public double StrongContentGuaranteeRefreshProbability { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<AbsolutePath> FileChangeTrackingExclusionRoots { get; set; }

        /// <inheritdoc />
        IReadOnlyList<AbsolutePath> ICacheConfiguration.FileChangeTrackingExclusionRoots => FileChangeTrackingExclusionRoots;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<AbsolutePath> FileChangeTrackingInclusionRoots { get; set; }

        /// <inheritdoc />
        IReadOnlyList<AbsolutePath> ICacheConfiguration.FileChangeTrackingInclusionRoots => FileChangeTrackingInclusionRoots;

        /// <inheritdoc />
        public bool UseDedupStore { get; set; }

        /// <inheritdoc />
        public bool ReplaceExistingFileOnMaterialization { get; set; }
    }
}
