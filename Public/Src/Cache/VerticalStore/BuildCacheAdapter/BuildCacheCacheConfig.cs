// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.MemoizationStore.Vsts;

namespace BuildXL.Cache.BuildCacheAdapter
{
#pragma warning disable CA1812 // internal class that is apparently never instantiated.
    internal class BuildCacheCacheConfig : BuildCacheServiceConfiguration
#pragma warning restore CA1812 // internal class that is apparently never instantiated.
    {
        /// <remarks>
        /// <see cref="BuildXL.Cache.Interfaces.CacheFactory"/> uses PropertyDescriptors to set values which don't work with private setters,
        /// Hence redefining properties for the fingerprint and content endpoints
        /// </remarks>
        public BuildCacheCacheConfig()
                : base(null, null)
        {
        }

        /// <summary>
        /// Path to the log file for the cache.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string CacheLogPath { get; set; }

        /// <summary>
        /// Gets the endpoint to talk to the fingerprint controller of a Build Cache Service.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public new string CacheServiceFingerprintEndpoint { get; set; }

        /// <summary>
        /// Gets the endpoint to talk to the content management controller of a Build Cache Service.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public new string CacheServiceContentEndpoint { get; set; }

        /// <summary>
        /// Name of one of the named caches owned by CASaaS.
        /// </summary>
        [DefaultValue(null)]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string CacheName { get; set; }

        /// <summary>
        /// The scenario name for the CAS service connected to (this allows the client to connect to the non-default cas
        /// service. Default is null).
        /// </summary>
        [DefaultValue(null)]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public string ScenarioName { get; set; }

        /// <summary>
        /// Connections to CASaasS per session.
        /// </summary>
        [DefaultValue(16)]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public uint ConnectionsPerSession { get; set; }

        /// <summary>
        /// Port for connecting to CASaaS through Grpc. Overriders GrpcPortFileName if set.
        /// </summary>
        [DefaultValue(0)]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public uint GrpcPort { get; set; }

        /// <summary>
        /// Custom name for memory-mapped file in whicht o look for GRPC port.
        /// </summary>
        [DefaultValue(null)]
        public string GrpcPortFileName { get; internal set; }

        /// <summary>
        /// How many seconds each call should wait for a CASaaS connection before retrying.
        /// </summary>
        [DefaultValue(5)]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public uint ConnectionRetryIntervalSeconds { get; set; }

        /// <summary>
        /// How many times each call should retry connecting to CASaaS before timing out.
        /// </summary>
        [DefaultValue(12)]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public uint ConnectionRetryCount { get; set; }

        /// <summary>
        /// Duration to wait for exclusive access to the cache directory before timing out.
        /// </summary>
        [DefaultValue(0)]
        public uint LogFlushIntervalSeconds { get; set; }

        public BuildCacheServiceConfiguration AsBuildCacheServiceConfigurationFile()
        {
            return new BuildCacheServiceConfiguration(CacheServiceFingerprintEndpoint, CacheServiceContentEndpoint)
            {
                CacheNamespace = CacheNamespace,
                DaysToKeepContentBags = DaysToKeepContentBags,
                DaysToKeepUnreferencedContent = DaysToKeepUnreferencedContent,
                IsCacheServiceReadOnly = IsCacheServiceReadOnly,
                MaxFingerprintSelectorsToFetch = MaxFingerprintSelectorsToFetch,
                UseAad = UseAad,
                UseBlobContentHashLists = UseBlobContentHashLists,
                SealUnbackedContentHashLists = SealUnbackedContentHashLists,
                FingerprintIncorporationEnabled = FingerprintIncorporationEnabled,
                MaxFingerprintsPerIncorporateRequest = MaxFingerprintsPerIncorporateRequest,
                MaxDegreeOfParallelismForIncorporateRequests = MaxDegreeOfParallelismForIncorporateRequests,
                HttpSendTimeoutMinutes = HttpSendTimeoutMinutes,
                DownloadBlobsThroughBlobStore = DownloadBlobsThroughBlobStore,
                UseDedupStore = UseDedupStore,
                OverrideUnixFileAccessMode = OverrideUnixFileAccessMode
            };
        }
    }
}
