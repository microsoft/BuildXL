// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using Newtonsoft.Json;

// ReSharper disable MemberCanBePrivate.Global
namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    /// Represents a data class that contains configuration data for a VSTS Build Cache Service.
    /// </summary>
    [DataContract]
    public class BuildCacheServiceConfiguration
    {
        /// <summary>
        /// Gets or sets the number of days to keep content before it is referenced by metadata.
        /// </summary>
        public const int DefaultDaysToKeepUnreferencedContent = 1;

        /// <summary>
        /// Default minimum number of days to keep content bags and referenced content.
        /// </summary>
        public const int DefaultDaysToKeepContentBags = 7;

        /// <summary>
        /// Default the range of additional days of expiry to be added to the expiration of content bags and referenced content.
        /// </summary>
        public const int DefaultRangeOfDaysToKeepContentBags = 2;

        /// <summary>
        /// Default value indicating whether the client talking to the
        /// service is doing so in a read-only manner or if it can allow writes into the cache.
        /// </summary>
        public const bool DefaultIsCacheServiceReadOnly = false;

        /// <summary>
        /// Default number of selectors to fetch from the remote service.
        /// </summary>
        public const int DefaultMaxFingerprintSelectorsToFetch = 500;

        /// <summary>
        /// Gets or sets the namespace of the cache being communicated with.
        /// </summary>
        public const string DefaultCacheNamespace = BuildCacheResourceIds.DefaultCacheNamespace;

        /// <summary>
        /// Default value indicating whether the client should attempt to seal unbacked ContentHashLists.
        /// </summary>
        public const bool DefaultSealUnbackedContentHashLists = false;

        /// <summary>
        /// Default value indicating whether or not to use the Production AAD SPS instance for authentication
        /// when connecting to the VSTS accounts specified in the config.
        /// </summary>
        public const bool DefaultUseAad = true;

        /// <summary>
        /// Default value indicating whether or not blob based metadata entries are used in the VSTS cache.
        /// </summary>
        public const bool DefaultUseBlobContentHashLists = false;

        /// <summary>
        /// Default value indicating whether strong fingerprints will be incorporated.
        /// This is a feature flag.
        /// </summary>
        public const bool DefaultIncorporationEnabled = false;

        /// <summary>
        /// Default max fingerprints allowed per chunk.
        /// </summary>
        public const int DefaultMaxFingerprintsPerIncorporateRequest = 500;

        /// <summary>
        /// Default number of minutes to wait for a response after sending an http request before timing out.
        /// </summary>
        public const int DefaultHttpSendTimeoutMinutes = 5;

        /// <summary>
        /// Default value indicating whether blobs are downloaded through BlobStore.
        /// </summary>
        public const bool DefaultDownloadBlobsThroughBlobStore = false;

        /// <summary>
        /// Default value indicating whether Dedup is enabled.
        /// </summary>
        public const bool DefaultUseDedupStore = false;

        /// <summary>
        /// Default value indicating whether Unix file access mode override is enabled.
        /// </summary>
        public const bool DefaultOverrideUnixFileAccessMode = false;


        /// <summary>
        /// Initializes a new instance of the <see cref="BuildCacheServiceConfiguration"/> class.
        /// </summary>
        [JsonConstructor]
        protected BuildCacheServiceConfiguration()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildCacheServiceConfiguration"/> class.
        /// </summary>
        public BuildCacheServiceConfiguration(string cacheServiceFingerprintEndpoint, string cacheServiceContentEndpoint)
            : this()
        {
            CacheServiceContentEndpoint = cacheServiceContentEndpoint;
            CacheServiceFingerprintEndpoint = cacheServiceFingerprintEndpoint;
        }

        /// <summary>
        /// Gets the endpoint to talk to the fingerprint controller of a Build Cache Service.
        /// </summary>
        [DataMember]
        public string CacheServiceFingerprintEndpoint { get; private set; }

        /// <summary>
        /// Gets the endpoint to talk to the content management controller of a Build Cache Service.
        /// </summary>
        [DataMember]
        public string CacheServiceContentEndpoint { get; private set; }

        /// <summary>
        /// Gets or sets the number of days to keep content before it is referenced by metadata.
        /// </summary>
        [DataMember]
        public int DaysToKeepUnreferencedContent { get; set; } = DefaultDaysToKeepUnreferencedContent;

        /// <summary>
        /// Gets or sets the minimum number of days to keep content bags and referenced content.
        /// </summary>
        [DataMember]
        public int DaysToKeepContentBags { get; set; } = DefaultDaysToKeepContentBags;

        /// <summary>
        /// Gets or sets the range of additional days of expiry to be added to the expiration of content bags and referenced content.
        /// </summary>
        /// <remarks>
        /// Since
        /// 1) the extra expiration is chosen randomly within this range and
        /// 2) a value's expiration only needs to be updated if it has fallen below the minimum,
        /// on average the update must only be sent twice during each of these intervals.
        /// </remarks>
        [DataMember]
        public int RangeOfDaysToKeepContentBags { get; set; } = DefaultRangeOfDaysToKeepContentBags;

        /// <summary>
        /// Gets or sets a value indicating whether the client talking to the
        /// service is doing so in a read-only manner or if it can allow writes into the cache.
        /// </summary>
        [DataMember]
        public bool IsCacheServiceReadOnly { get; set; } = DefaultIsCacheServiceReadOnly;

        /// <summary>
        /// Gets or sets the number of selectors to fetch from the remote service.
        /// </summary>
        [DataMember]
        public int MaxFingerprintSelectorsToFetch { get; set; } = DefaultMaxFingerprintSelectorsToFetch;

        /// <summary>
        /// Gets or sets the namespace of the cache being communicated with. Default: "default"
        /// </summary>
        [DataMember]
        public string CacheNamespace { get; set; } = DefaultCacheNamespace;

        /// <summary>
        /// Gets or sets a value indicating whether the client should attempt to seal unbacked ContentHashLists.
        /// </summary>
        [DataMember]
        public bool SealUnbackedContentHashLists { get; set; } = DefaultSealUnbackedContentHashLists;

        /// <summary>
        /// Gets or sets a value indicating whether or not to use the Production AAD SPS instance for authentication
        /// when connecting to the VSTS accounts specified in the config.
        /// </summary>
        [DataMember]
        public bool UseAad { get; set; } = DefaultUseAad;

        /// <summary>
        /// Gets or sets a value indicating whether or not blob based metadata entries are used in the VSTS cache.
        /// </summary>
        [DataMember]
        public bool UseBlobContentHashLists { get; set; } = DefaultUseBlobContentHashLists;

        /// <summary>
        /// Gets or sets a value indicating whether strong fingerprints will be incorporated.
        /// This is a feature flag. Default: Disabled
        /// </summary>
        [DataMember]
        public bool FingerprintIncorporationEnabled { get; set; } = DefaultIncorporationEnabled;

        /// <summary>
        /// Gets or sets the maximum number of fingerprints chunks sent in parallel. Default: System.Environment.ProcessorCount
        /// </summary>
        [DataMember]
        public int MaxDegreeOfParallelismForIncorporateRequests { get; set; } = System.Environment.ProcessorCount;

        /// <summary>
        /// Gets or sets the max fingerprints allowed per chunk. Default: 500
        /// </summary>
        [DataMember]
        public int MaxFingerprintsPerIncorporateRequest { get; set; } = DefaultMaxFingerprintsPerIncorporateRequest;

        /// <summary>
        /// Gets or sets the number of minutes to wait for a response after sending an http request before timing out. Default: 5 minutes
        /// </summary>
        [DataMember]
        public int HttpSendTimeoutMinutes { get; set; } = DefaultHttpSendTimeoutMinutes;

        /// <summary>
        /// Gets or sets whether blobs are downloaded through BlobStore.
        /// </summary>
        [DataMember]
        public bool DownloadBlobsThroughBlobStore { get; set; } = DefaultDownloadBlobsThroughBlobStore;

        /// <summary>
        /// Gets or sets whether Dedup is enabled.
        /// </summary>
        [DataMember]
        public bool UseDedupStore { get; set; } = DefaultUseDedupStore;

        /// <summary>
        /// Gets or sets whether to override Unix file access modes.
        /// </summary>
        [DataMember]
        public bool OverrideUnixFileAccessMode { get; set; } = DefaultOverrideUnixFileAccessMode;
    }
}
