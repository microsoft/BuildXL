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
        private const int DefaultMaxConcurrentCopyOperations = 512;

        internal static readonly int[] DefaultRetryIntervalForCopiesMs = 
            new int[]
            {
                // retry the first 2 times quickly.
                20,
                200,

                // then back-off exponentially.
                1000,
                5000,
                10000,
                30000,

                // Borrowed from Empirical CacheV2 determined to be appropriate for general remote server restarts.
                60000,
                120000,
            };

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

        private int _redisMemoizationExpiryTimeMinutes;

        /// <summary>
        /// TTL to be set in Redis for memoization entries.
        /// </summary>
        [DataMember]
        public int RedisMemoizationExpiryTimeMinutes {
            get => _redisMemoizationExpiryTimeMinutes == 0 ? ContentHashBumpTimeMinutes : _redisMemoizationExpiryTimeMinutes;
            set => _redisMemoizationExpiryTimeMinutes = value;
        }

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

        [DataMember]
        public int MaxShutdownDurationInMinutes { get; set; } = 30;

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

        [DataMember]
        public int? SelfCheckProgressReportingIntervalInMinutes { get; set; }

        [DataMember]
        public int? SelfCheckDelayInMilliseconds { get; set; }

        [DataMember]
        public int? SelfCheckDefaultHddDelayInMilliseconds { get; set; }

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

        /// <summary>
        /// Amount of entries to compute evictability metric for in a single pass. The larger this is, the faster the
        /// candidate pool fills up, but also the slower it is to produce a candidate. Helps control how fast we need
        /// to produce candidates.
        /// </summary>
        [DataMember]
        public int EvictionWindowSize { get; set; } = 500;

        /// <summary>
        /// Amount of entries to compute evictability metric for before determining eviction order. The larger this is,
        /// the slower and more resources eviction takes, but also the more accurate it becomes.
        /// </summary>
        /// <remarks>
        /// Two pools are kept in memory at the same time, so we effectively keep double the amount of data in memory.
        /// </remarks>
        [DataMember]
        public int EvictionPoolSize { get; set; } = 5000;

        /// <summary>
        /// A candidate must have an age older than this amount, or else it won't be evicted.
        /// </summary>
        [DataMember]
        public TimeSpan EvictionMinAge { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Fraction of the pool considered trusted to be in the accurate order.
        /// </summary>
        [DataMember]
        public float EvictionRemovalFraction { get; set; } = 0.015355f;

        private int[] _retryIntervalForCopiesMs =
            new int[]
            {
                // retry the first 2 times quickly.
                20,
                200,

                // then back-off exponentially.
                1000,
                5000,
                10000,
                30000,

                // Borrowed from Empirical CacheV2 determined to be appropriate for general remote server restarts.
                60000,
                120000,
            };

        /// <summary>
        /// Delays for retries for file copies
        /// </summary>
        [DataMember]
        public int[] RetryIntervalForCopiesMs
        {
            get => _retryIntervalForCopiesMs ?? DefaultRetryIntervalForCopiesMs;
            set => _retryIntervalForCopiesMs = value;
        }

        public IReadOnlyList<TimeSpan> RetryIntervalForCopies => RetryIntervalForCopiesMs.Select(ms => TimeSpan.FromMilliseconds(ms)).ToList();

        /// <summary>
        /// Controls the maximum total number of copy retry attempts
        /// </summary>
        public int MaxRetryCount { get; set; } = 32;
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

        /// <summary>
        /// Upper bound on number of cached GRPC clients.
        /// </summary>
        [DataMember]
        public int MaxGrpcClientCount { get; set; } = DefaultMaxConcurrentCopyOperations;

        /// <summary>
        /// Maximum cached age for GRPC clients.
        /// </summary>
        [DataMember]
        public int MaxGrpcClientAgeMinutes { get; set; } = 55;

        /// <summary>
        /// Time between GRPC cache cleanups.
        /// </summary>
        [DataMember]
        public int GrpcClientCleanupDelayMinutes { get; set; } = 17;
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
        public bool IsBandwidthCheckEnabled { get; set; } = true;

        [DataMember]
        public double? MinimumSpeedInMbPerSec { get; set; } = null;

        [DataMember]
        public int BandwidthCheckIntervalSeconds { get; set; } = 60;

        [DataMember]
        public double MaxBandwidthLimit { get; set; } = double.MaxValue;

        [DataMember]
        public double BandwidthLimitMultiplier { get; set; } = 1;

        [DataMember]
        public int HistoricalBandwidthRecordsStored { get; set; } = 64;
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

        /// <summary>
        /// Disabling reconciliation is an unsafe option that can cause builds to fail because the machine's state can be off compared to the LLS's state.
        /// Please do not set this property for long period of time. 
        /// </summary>
        [DataMember]
        public bool Unsafe_DisableReconciliation { get; set; } = false;

        [DataMember]
        public int ReconciliationCycleFrequencyMinutes { get; set; } = 30;

        [DataMember]
        public int ReconciliationMaxCycleSize { get; set; } = 100000;

        [DataMember]
        public bool IsContentLocationDatabaseEnabled { get; set; } = false;

        [DataMember]
        public bool StoreClusterStateInDatabase { get; set; } = true;

        [DataMember]
        public bool IsMachineReputationEnabled { get; set; } = true;

        [DataMember]
        public bool? UseIncrementalCheckpointing { get; set; }

        [DataMember]
        public int? IncrementalCheckpointDegreeOfParallelism { get; set; }

        [DataMember]
        public int? ContentLocationDatabaseGcIntervalMinutes { get; set; }

        [DataMember]
        public int? ContentLocationDatabaseEntryTimeToLiveMinutes { get; set; }

        [DataMember]
        public bool? ContentLocationDatabaseCacheEnabled { get; set; }

        [DataMember]
        public int? ContentLocationDatabaseFlushDegreeOfParallelism { get; set; }

        [DataMember]
        public int? ContentLocationDatabaseFlushTransactionSize { get; set; }

        [DataMember]
        public bool? ContentLocationDatabaseFlushSingleTransaction { get; set; }

        [DataMember]
        public double? ContentLocationDatabaseFlushPreservePercentInMemory { get; set; }

        [DataMember]
        public int? ContentLocationDatabaseCacheMaximumUpdatesPerFlush { get; set; }

        [DataMember]
        public TimeSpan? ContentLocationDatabaseCacheFlushingMaximumInterval { get; set; }

        [DataMember]
        public int? FullRangeCompactionIntervalMinutes { get; set; }

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
        public int? EventBatchSize { get; set; }

        [DataMember]
        public int? EventProcessingMaxQueueSize { get; set; }

        [DataMember]
        public string[] AzureStorageSecretNames { get; set; }

        [DataMember]
        public string AzureStorageSecretName { get; set; }

        [DataMember]
        public bool AzureBlobStorageUseSasTokens { get; set; } = false;

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
        public int? RestoreCheckpointAgeThresholdMinutes { get; set; }

        [DataMember]
        public int? MachineExpiryMinutes { get; set; }

        [DataMember]
        public bool CleanRandomFilesAtRoot { get; set; } = false;

        // Files smaller than this will use the untrusted hash
        [DataMember]
        public int TrustedHashFileSizeBoundary = 100000;

        [DataMember]
        public long ParallelHashingFileSizeBoundary { get; set; } = -1;

        [DataMember]
        public long CacheFileExistenceTimeoutInCopySec { get; set; } = -1;

        [DataMember]
        public long CacheFileExistenceSizeBytes { get; set; } = -1;


        [DataMember]
        public bool UseRedundantPutFileShortcut { get; set; } = false;

        [DataMember]
        public int MaxConcurrentCopyOperations { get; set; } = DefaultMaxConcurrentCopyOperations;

        [DataMember]
        public int MaxConcurrentProactiveCopyOperations { get; set; } = DefaultMaxConcurrentCopyOperations;

        /// <summary>
        /// Gets or sets whether to override Unix file access modes.
        /// </summary>
        [DataMember]
        public bool OverrideUnixFileAccessMode { get; set; } = false;

        [DataMember]
        public bool TraceFileSystemContentStoreDiagnosticMessages { get; set; } = false;

        /// <summary>
        /// Valid values: Disabled, InsideRing, OutsideRing, Both (See ProactiveCopyMode enum)
        /// </summary>
        [DataMember]
        public string ProactiveCopyMode { get; set; } = "Disabled";

        [DataMember]
        public bool PushProactiveCopies { get; set; } = false;

        [DataMember]
        public bool ProactiveCopyOnPin { get; set; } = false;

        [DataMember]
        public int ProactiveCopyLocationsThreshold { get; set; } = 1;

        [DataMember]
        public int MaximumConcurrentPutFileOperations { get; set; } = 512;

        [DataMember]
        public bool EnableMetadataStore { get; set; } = false;

        [DataMember]
        public int MaximumNumberOfMetadataEntriesToStore { get; set; } = 500_000;

        [DataMember]
        public bool UseRedisMetadataStore{ get; set; } = false;

        [DataMember]
        public int TimeoutForProactiveCopiesMinutes { get; set; } = 15;

        #endregion

        /// <summary>
        /// Gets the secret name to connect to Redis for a particular CloudBuild stamp.
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
