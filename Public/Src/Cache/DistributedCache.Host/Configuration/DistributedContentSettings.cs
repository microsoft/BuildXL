// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Feature flag settings for an L2 Content Cache.
    /// </summary>
    [DataContract]
    public class DistributedContentSettings
    {
        [JsonConstructor]
        private DistributedContentSettings()
        {
        }

        public static DistributedContentSettings CreateDisabled()
        {
            return new DistributedContentSettings
            {
                IsDistributedContentEnabled = false,
                ConnectionSecretNameMap = new Dictionary<string, string>()
            };
        }

        public static DistributedContentSettings CreateEnabled(IDictionary<string, string> connectionSecretNameMap, bool isGrpcCopierEnabled = false)
        {
            return new DistributedContentSettings(connectionSecretNameMap)
            {
                IsDistributedContentEnabled = true,
                IsGrpcCopierEnabled = isGrpcCopierEnabled
            };
        }

        public static DistributedContentSettings CreateForCloudBuildCacheCacheFactory(DistributedContentSettings distributedSettings)
        {
            return new DistributedContentSettings
            {
                IsBandwidthCheckEnabled = distributedSettings.IsBandwidthCheckEnabled,
                IsDistributedEvictionEnabled = distributedSettings.IsDistributedEvictionEnabled,
                AlternateDriveMap =
                    distributedSettings.GetAutopilotAlternateDriveMap().ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };
        }

        private DistributedContentSettings(IDictionary<string, string> connectionSecretNameMap)
        {
            ConnectionSecretNameMap = connectionSecretNameMap;
        }

        /// <summary>
        /// Feature flag to turn on distributed content tracking (L2/datacenter cache).
        /// </summary>
        [DataMember]
        public bool IsDistributedContentEnabled { get; set; }

        /// <summary>
        /// Configurable Keyspace Prefixes
        /// </summary>
        [DataMember]
        public string KeySpacePrefix { get; set; } = "CBPrefix";

        /// <summary>
        /// Bump content expiry times when adding replicas
        /// </summary>
        [DataMember]
        public int ContentHashBumpTimeMinutes { get; set; } = 2880;

        /// <summary>
        /// The map of environment to connection secrets
        /// </summary>
        [DataMember]
        private IDictionary<string, string> ConnectionSecretNameMap { get; set; }

        /// <summary>
        /// The map of environment to connection secret pairs (one for content locations and one for machine ids)
        /// </summary>
        /// <remarks>Internal for access by config validation rules</remarks>
        [DataMember]
        public IDictionary<string, RedisContentSecretNames> ConnectionSecretNamesMap { get; set; }

        /// <summary>
        /// The map of drive paths to alternate paths to access them
        /// </summary>
        [DataMember]
        private IDictionary<string, string> AlternateDriveMap { get; set; }

        [DataMember]
        public string ContentAvailabilityGuarantee { get; set; }

        [DataMember]
        public int RedisBatchPageSize { get; set; } = 500;

        // TODO: file a work item to remove the flag!
        [DataMember]
        public bool CheckLocalFiles { get; set; } = false;

        /// <summary>
        /// Whether to use old (original) implementation of QuotaKeeper or to use the new one.
        /// </summary>
        [DataMember]
        public bool UseLegacyQuotaKeeperImplementation { get; set; } = true;

        /// <summary>
        /// If true, then quota keeper will check the current content directory size and start content eviction at startup if the threshold is reached.
        /// </summary>
        [DataMember]
        public bool StartPurgingAtStartup { get; set; } = true;

        /// <summary>
        /// If true, then content store will start a self-check to validate that the content in cache is valid at startup.
        /// </summary>
        [DataMember]
        public bool StartSelfCheckAtStartup { get; set; } = false;

        /// <summary>
        /// An interval between self checks performed by a content store to make sure that all the data on disk matches it's hashes.
        /// </summary>
        [DataMember]
        public int SelfCheckFrequencyInMinutes { get; set; } = (int)TimeSpan.FromDays(1).TotalMinutes;

        /// <summary>
        /// An epoch used for reseting self check of a content directory.
        /// </summary>
        [DataMember]
        public string SelfCheckEpoch { get; set; } = "E0";

        /// <summary>
        /// Whether to use native (unmanaged) file enumeration or not.
        /// </summary>
        [DataMember]
        public bool UseNativeBlobEnumeration { get; set; } = false;

        [DataMember]
        public bool IsRepairHandlingEnabled { get; set; } = false;

        [DataMember]
        public bool IsTouchEnabled { get; set; } = false;

        [DataMember]
        public bool UseMdmCounters { get; set; } = true;

        /// <summary>
        /// The TTL of blobs in Redis. Setting to 0 will disable blobs.
        /// </summary>
        [DataMember]
        public int BlobExpiryTimeMinutes { get; set; } = 0;

        /// <summary>
        /// TMax size of blobs in Redis. Setting to 0 will disable blobs.
        /// </summary>
        [DataMember]
        public long MaxBlobSize { get; set; } = 1024 * 4;

        /// <summary>
        /// Max capacity that blobs can occupy in Redis. Setting to 0 will disable blobs.
        /// </summary>
        [DataMember]
        public long MaxBlobCapacity { get; set; } = 1024 * 1024 * 1024;

        #region Grpc Copier
        /// <summary>
        /// Use GRPC for file copies between CASaaS.
        /// </summary>
        [DataMember]
        public bool IsGrpcCopierEnabled { get; set; } = false;

        /// <summary>
        /// Whether or not GZip is enabled for GRPC copies.
        /// </summary>
        [DataMember]
        public bool UseCompressionForCopies { get; set; } = false;
        #endregion

        #region Distributed Eviction
        [DataMember]
        public bool IsDistributedEvictionEnabled { get; set; } = false;

        [DataMember]
        public int? ReplicaCreditInMinutes { get; set; } = 180;

        [DataMember]
        public int? MinReplicaCountToSafeEvict { get; set; }

        [DataMember]
        public int? MinReplicaCountToImmediateEvict { get; set; }
        #endregion

        #region Bandwidth Check
        [DataMember]
        public bool IsBandwidthCheckEnabled { get; set; } = false;

        [DataMember]
        public double MinimumSpeedInMbPerSec { get; set; } = -1.0;

        [DataMember]
        public int BandwidthCheckIntervalSeconds { get; set; } = 60;
        #endregion

        #region Pin Better
        [DataMember]
        public bool IsPinBetterEnabled { get; set; } = false;

        [DataMember]
        public double? PinRisk { get; set; }

        [DataMember]
        public double? MachineRisk { get; set; }

        [DataMember]
        public double? FileRisk { get; set; }

        [DataMember]
        public int? MaxIOOperations { get; set; }
        #endregion

        #region Pin Caching
        [DataMember]
        public bool IsPinCachingEnabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the starting retention time for content hash entries in the pin cache.
        /// </summary>
        [DataMember]
        public int? PinCacheReplicaCreditRetentionMinutes { get; set; }

        /// <summary>
        /// Gets or sets the decay applied for replicas to <see cref="PinCacheReplicaCreditRetentionMinutes"/>. Must be between 0 and 0.9.
        /// For each replica 1...n, with decay d, the additional retention is depreciated by d^n (i.e. only  <see cref="PinCacheReplicaCreditRetentionMinutes"/> * d^n is added to the total retention
        /// based on the replica).
        /// </summary>
        [DataMember]
        public double? PinCacheReplicaCreditRetentionDecay { get; set; }
        #endregion

        #region Local Location Store

        #region Distributed Central Storage

        [DataMember]
        public bool UseDistributedCentralStorage { get; set; } = false;

        [DataMember]
        public int MaxCentralStorageRetentionGb { get; set; } = 25;

        [DataMember]
        public int CentralStoragePropagationDelaySeconds { get; set; } = 5;

        [DataMember]
        public int CentralStoragePropagationIterations { get; set; } = 3;

        [DataMember]
        public int CentralStorageMaxSimultaneousCopies { get; set; } = 10;

        #endregion

        [DataMember]
        public bool IsMasterEligible { get; set; } = false;

        [DataMember]
        public bool IsRedisGarbageCollectionEnabled { get; set; } = false;

        [DataMember]
        public bool? IsReconciliationEnabled { get; set; }

        [DataMember]
        public bool IsContentLocationDatabaseEnabled { get; set; } = false;

        [DataMember]
        public bool StoreClusterStateInDatabase { get; set; } = true;

        [DataMember]
        public bool IsMachineReputationEnabled { get; set; } = false;

        [DataMember]
        public bool? UseIncrementalCheckpointing { get; set; }

        [DataMember]
        public int? ContentLocationDatabaseGcIntervalMinutes { get; set; }

        [DataMember]
        public int? ContentLocationDatabaseEntryTimeToLiveMinutes { get; set; }

        // Key Vault Settings
        [DataMember]
        public string KeyVaultSettingsString { get; set; }

        [DataMember]
        public int SecretsRetrievalRetryCount { get; set; } = 5;

        [DataMember]
        public int SecretsRetrievalMinBackoffSeconds { get; set; } = 10;

        [DataMember]
        public int SecretsRetrievalMaxBackoffSeconds { get; set; } = 60;

        [DataMember]
        public int SecretsRetrievalDeltaBackoffSeconds { get; set; } = 10;

        [DataMember]
        public string EventHubSecretName { get; set; }

        [DataMember]
        public int? MaxEventProcessingConcurrency { get; set; }

        [DataMember]
        public string[] AzureStorageSecretNames { get; set; }

        [DataMember]
        public string AzureStorageSecretName { get; set; }

        [DataMember]
        public string EventHubEpoch { get; set; } = ".LLS_V1.2";

        [DataMember]
        public string GlobalRedisSecretName { get; set; }

        [DataMember]
        public string SecondaryGlobalRedisSecretName { get; set; }

        [DataMember]
        public bool? MirrorClusterState { get; set; }

        [DataMember]
        public double? HeartbeatIntervalMinutes { get; set; }

        [DataMember]
        public double? CreateCheckpointIntervalMinutes { get; set; }

        [DataMember]
        public double? RestoreCheckpointIntervalMinutes { get; set; }

        [DataMember]
        public int? SafeToLazilyUpdateMachineCountThreshold { get; set; }

        [DataMember]
        public int? CentralStorageOperationTimeoutInMinutes { get; set; }

        /// <summary>
        /// Valid values: LocalLocationStore, Redis, Both (see ContentStore.Distributed.ContentLocationMode)
        /// </summary>
        [DataMember]
        public string ContentLocationReadMode { get; set; }

        /// <summary>
        /// Valid values: LocalLocationStore, Redis, Both (see ContentStore.Distributed.ContentLocationMode)
        /// </summary>
        [DataMember]
        public string ContentLocationWriteMode { get; set; }

        [DataMember]
        public int? LocationEntryExpiryMinutes { get; set; }

        [DataMember]
        public int? MachineExpiryMinutes { get; set; }

        [DataMember]
        public bool CleanRandomFilesAtRoot { get; set; } = false;

        [DataMember]
        public bool UseTrustedHash { get; set; } = false;

        // Files smaller than this will use the untrusted hash
        [DataMember]
        public int TrustedHashFileSizeBoundary = -1;

        [DataMember]
        public long ParallelHashingFileSizeBoundary { get; set; } = -1;

        [DataMember]
        public long CacheFileExistenceTimeoutInCopySec { get; set; } = -1;

        [DataMember]
        public long CacheFileExistenceSizeBytes { get; set; } = -1;

        [DataMember]
        public bool EmptyFileHashShortcutEnabled { get; set; } = false;

        [DataMember]
        public int MaxConcurrentCopyOperations { get; set; } = 512;

        #endregion

        /// <summary>
        /// Gets the secret name to connect to redis for a particular CloudBuild stamp.
        /// </summary>
        /// <param name="stampId">The ID of the stamp.</param>
        /// <returns>The secret name in the AP secret store.</returns>
        public RedisContentSecretNames GetRedisConnectionSecretNames(string stampId)
        {
            if (!IsDistributedContentEnabled)
            {
                return null;
            }

            if (ConnectionSecretNamesMap != null)
            {
                return ConnectionSecretNamesMap.Single(kvp => Regex.IsMatch(stampId, kvp.Key, RegexOptions.IgnoreCase))
                    .Value;
            }

            return new RedisContentSecretNames(
                ConnectionSecretNameMap.Single(kvp => Regex.IsMatch(stampId, kvp.Key, RegexOptions.IgnoreCase)).Value);
        }

        public Tuple<double, int> GetBandwidthCheckSettings()
        {
            return IsDistributedContentEnabled && IsBandwidthCheckEnabled
                ? Tuple.Create(MinimumSpeedInMbPerSec, BandwidthCheckIntervalSeconds)
                : null;
        }

        public IReadOnlyDictionary<string, string> GetAutopilotAlternateDriveMap()
        {
            if (AlternateDriveMap != null)
            {
                return new ReadOnlyDictionary<string, string>(AlternateDriveMap);
            }
            else
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
