// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
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
        public DistributedContentSettings()
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
        /// The amount of time for nagling GetBulk (locations) for proactive copy operations
        /// </summary>
        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double ProactiveCopyGetBulkIntervalSeconds { get; set; } = 10;

        /// <summary>
        /// The size of nagle batch for proactive copy get bulk
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ProactiveCopyGetBulkBatchSize { get; set; } = 20;

        /// <summary>
        /// Configurable Keyspace Prefixes
        /// </summary>
        [DataMember]
        public string KeySpacePrefix { get; set; } = "CBPrefix";

        /// <summary>
        /// Adds suffix to resource names to allow instances in different ring to
        /// share the same resource without conflicts. In particular this will add the
        /// ring suffix to Azure blob container name and Redis keyspace (which is included
        /// in event hub epoch). 
        /// </summary>
        [DataMember]
        public bool UseRingIsolation { get; set; } = false;

        /// <summary>
        /// Bump content expiry times when adding replicas
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        // Obsolete: Used by old redis only.
        public int ContentHashBumpTimeMinutes { get; set; } = 2880;

        private int _redisMemoizationExpiryTimeMinutes;

        /// <summary>
        /// TTL to be set in Redis for memoization entries.
        /// </summary>
        [DataMember]
        public int RedisMemoizationExpiryTimeMinutes
        {
            get => _redisMemoizationExpiryTimeMinutes == 0 ? ContentHashBumpTimeMinutes : _redisMemoizationExpiryTimeMinutes;
            set => _redisMemoizationExpiryTimeMinutes = value;
        }

        /// <summary>
        /// The map of environment to connection secrets
        /// </summary>
        [DataMember]
        private IDictionary<string, string> ConnectionSecretNameMap { get; set; }

        /// <summary>
        /// The map of drive paths to alternate paths to access them
        /// </summary>
        [DataMember]
        private IDictionary<string, string> AlternateDriveMap { get; set; }

        [DataMember]
        public string ContentAvailabilityGuarantee { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int RedisBatchPageSize { get; set; } = 500;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RedisConnectionErrorLimit { get; set; }

        [DataMember]
        public bool? TraceRedisFailures { get; set; }

        [DataMember]
        public bool? TraceRedisTransientFailures { get; set; }

        [DataMember]
        [Validation.Range(-1, int.MaxValue)]
        public int? RedisGetBlobTimeoutMilliseconds { get; set; }

        // TODO: file a work item to remove the flag!
        [DataMember]
        public bool CheckLocalFiles { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
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
        [Validation.Range(1, int.MaxValue)]
        public int SelfCheckFrequencyInMinutes { get; set; } = (int)TimeSpan.FromDays(1).TotalMinutes;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SelfCheckProgressReportingIntervalInMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SelfCheckDelayInMilliseconds { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
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

        [DataMember]
        public bool UseContextualEntryDatabaseOperationLogging { get; set; } = false;

        [DataMember]
        public bool LogReconciliationHashes { get; set; } = false;

        /// <summary>
        /// The TTL of blobs in Redis. Setting to 0 will disable blobs.
        /// </summary>
        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int BlobExpiryTimeMinutes { get; set; } = 0;

        /// <summary>
        /// Max size of blobs in Redis. Setting to 0 will disable blobs.
        /// </summary>
        [DataMember]
        [Validation.Range(0, long.MaxValue)]
        public long MaxBlobSize { get; set; } = 1024 * 4;

        /// <summary>
        /// Max capacity that blobs can occupy in Redis. Setting to 0 will disable blobs.
        /// </summary>
        [DataMember]
        [Validation.Range(0, long.MaxValue)]
        public long MaxBlobCapacity { get; set; } = 1024 * 1024 * 1024;

        /// <summary>
        /// Amount of entries to compute evictability metric for in a single pass. The larger this is, the faster the
        /// candidate pool fills up, but also the slower it is to produce a candidate. Helps control how fast we need
        /// to produce candidates.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int EvictionWindowSize { get; set; } = 500;

        /// <summary>
        /// Amount of entries to compute evictability metric for before determining eviction order. The larger this is,
        /// the slower and more resources eviction takes, but also the more accurate it becomes.
        /// </summary>
        /// <remarks>
        /// Two pools are kept in memory at the same time, so we effectively keep double the amount of data in memory.
        /// </remarks>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int EvictionPoolSize { get; set; } = 5000;

        /// <summary>
        /// A candidate must have an age older than this amount, or else it won't be evicted.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? EvictionMinAgeMinutes { get; set; }

        /// <summary>
        /// Fraction of the pool considered trusted to be in the accurate order.
        /// </summary>
        [DataMember]
        [Validation.Range(0, 1, maxInclusive: false)]
        public float EvictionRemovalFraction { get; set; } = 0.015355f;

        /// <summary>
        /// Fraction of the pool that can be trusted to be spurious at each iteration
        /// </summary>
        [DataMember]
        [Validation.Range(0, 1)]
        public float EvictionDiscardFraction { get; set; } = 0;

        /// <summary>
        /// Configures whether to use full eviction sort logic
        /// </summary>
        [DataMember]
        public bool UseFullEvictionSort { get; set; } = false;

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double? ThrottledEvictionIntervalMinutes { get; set; }

        /// <summary>
        /// Configures whether ages of content are updated when sorting during eviction
        /// </summary>
        [DataMember]
        public bool UpdateStaleLocalLastAccessTimes { get; set; } = false;

        /// <summary>
        /// Configures whether to use new tiered eviction logic or not.
        /// </summary>
        [DataMember]
        public bool UseTieredDistributedEviction { get; set; } = false;

        [DataMember]
        public bool PrioritizeDesignatedLocationsOnCopies { get; set; } = false;

        [DataMember]
        public bool DeprioritizeMasterOnCopies { get; set; } = false;

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int CopyAttemptsWithRestrictedReplicas { get; set; } = 0;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int RestrictedCopyReplicaCount { get; set; } = 3;

        /// <summary>
        /// After the first raided redis instance completes, the second instance is given a window of time to complete before the retries are cancelled.
        /// Default to always wait for both instances to complete.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RetryWindowSeconds { get; set; }

        /// <summary>
        /// If this variable is set we perform periodic logging to a file, indicating CaSaaS is still running and accepting operations.
        /// We then print the duration CaSaaS was just down, in the log message stating service has started.
        /// Default with the variable being null, no periodic logging will occur, and CaSaaS start log message does not include duration of last down time.
        /// </summary>
        [DataMember]
        public int? ServiceRunningLogInSeconds { get; set; }

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
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
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
        [Validation.Range(1, int.MaxValue)]
        public int MaxGrpcClientCount { get; set; } = DefaultMaxConcurrentCopyOperations;

        [DataMember]
        public bool UseUnsafeByteStringConstruction { get; set; } = false;

        /// <summary>
        /// Maximum cached age for GRPC clients.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int MaxGrpcClientAgeMinutes { get; set; } = 55;
        #endregion

        #region Distributed Eviction
        [DataMember]
        public bool IsDistributedEvictionEnabled { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ReplicaCreditInMinutes { get; set; } = 180;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MinReplicaCountToSafeEvict { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MinReplicaCountToImmediateEvict { get; set; }
        #endregion

        #region Bandwidth Check
        [DataMember]
        public bool IsBandwidthCheckEnabled { get; set; } = true;

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? MinimumSpeedInMbPerSec { get; set; } = null;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int BandwidthCheckIntervalSeconds { get; set; } = 60;

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double MaxBandwidthLimit { get; set; } = double.MaxValue;

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double BandwidthLimitMultiplier { get; set; } = 1;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int HistoricalBandwidthRecordsStored { get; set; } = 64;
        #endregion

        #region Pin Better
        [DataMember]
        public bool IsPinBetterEnabled { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? PinMinUnverifiedCount { get; set; }

        [DataMember]
        [Validation.Range(0, 1, minInclusive: false, maxInclusive: false)]
        public double? MachineRisk { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MaxIOOperations { get; set; }
        #endregion

        #region Local Location Store

        #region Distributed Central Storage

        [DataMember]
        public bool UseDistributedCentralStorage { get; set; } = false;

        [DataMember]
        public bool UseSelfCheckSettingsForDistributedCentralStorage { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int MaxCentralStorageRetentionGb { get; set; } = 25;

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int CentralStoragePropagationDelaySeconds { get; set; } = 5;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int CentralStoragePropagationIterations { get; set; } = 3;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int CentralStorageMaxSimultaneousCopies { get; set; } = 10;

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? DistributedCentralStoragePeerToPeerCopyTimeoutSeconds { get; set; } = null;

        #endregion

        [DataMember]
        public bool IsMasterEligible { get; set; } = false;

        /// <summary>
        /// Disabling reconciliation is an unsafe option that can cause builds to fail because the machine's state can be off compared to the LLS's state.
        /// Please do not set this property for long period of time. 
        /// </summary>
        [DataMember]
        public bool Unsafe_DisableReconciliation { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ReconciliationCycleFrequencyMinutes { get; set; } = 30;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ReconciliationMaxCycleSize { get; set; } = 100_000;

        [DataMember]
        public int? ReconciliationMaxRemoveHashesCycleSize { get; set; } = null;

        [DataMember]
        [Validation.Range(0, 1)]
        public double? ReconciliationMaxRemoveHashesAddPercentage { get; set; } = null;

        [DataMember]
        public bool IsContentLocationDatabaseEnabled { get; set; } = false;

        [DataMember]
        public bool StoreClusterStateInDatabase { get; set; } = true;

        [DataMember]
        public bool IsMachineReputationEnabled { get; set; } = true;

        [DataMember]
        public bool? UseIncrementalCheckpointing { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? IncrementalCheckpointDegreeOfParallelism { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ContentLocationDatabaseGcIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ContentLocationDatabaseEntryTimeToLiveMinutes { get; set; }

        [DataMember]
        public bool ContentLocationDatabaseLogsBackupEnabled { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ContentLocationDatabaseLogsBackupRetentionMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? FullRangeCompactionIntervalMinutes { get; set; }

        [DataMember]
        public string FullRangeCompactionVariant { get; set; }

        [DataMember]
        [Validation.Range(1, byte.MaxValue)]
        public byte? FullRangeCompactionByteIncrementStep { get; set; }

        [DataMember]
        [Validation.Range(1, long.MaxValue)]
        public long? ContentLocationDatabaseEnumerateSortedKeysFromStorageBufferSize { get; set; }

        [DataMember]
        [Validation.Range(1, long.MaxValue)]
        public long? ContentLocationDatabaseEnumerateEntriesWithSortedKeysFromStorageBufferSize { get; set; }

        // Key Vault Settings
        [DataMember]
        public string KeyVaultSettingsString { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int SecretsRetrievalRetryCount { get; set; } = 5;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int SecretsRetrievalMinBackoffSeconds { get; set; } = 10;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int SecretsRetrievalMaxBackoffSeconds { get; set; } = 60;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int SecretsRetrievalDeltaBackoffSeconds { get; set; } = 10;

        [DataMember]
        public string EventHubSecretName { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MaxEventProcessingConcurrency { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? EventBatchSize { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
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
        [Validation.Enum(typeof(Severity), allowNull: true)]
        public string RedisInternalLogSeverity { get; set; }

        [DataMember]
        public bool? MirrorClusterState { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? HeartbeatIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? CreateCheckpointIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RestoreCheckpointIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SafeToLazilyUpdateMachineCountThreshold { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? CentralStorageOperationTimeoutInMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? LocationEntryExpiryMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RestoreCheckpointAgeThresholdMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? TouchFrequencyMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MachineStateRecomputeIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MachineActiveToClosedIntervalMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MachineActiveToExpiredIntervalMinutes { get; set; }

        // Files smaller than this will use the untrusted hash
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int TrustedHashFileSizeBoundary = 100000;

        [DataMember]
        [Validation.Range(-1, long.MaxValue)]
        public long ParallelHashingFileSizeBoundary { get; set; } = -1;

        [DataMember]
        [Validation.Range(-1, long.MaxValue)]
        public long CacheFileExistenceTimeoutInCopySec { get; set; } = -1;

        [DataMember]
        [Validation.Range(-1, long.MaxValue)]
        public long CacheFileExistenceSizeBytes { get; set; } = -1;

        [DataMember]
        public bool UseRedundantPutFileShortcut { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int MaxConcurrentCopyOperations { get; set; } = DefaultMaxConcurrentCopyOperations;

        /// <summary>
        /// Gets or sets whether to override Unix file access modes.
        /// </summary>
        [DataMember]
        public bool OverrideUnixFileAccessMode { get; set; } = false;

        [DataMember]
        public bool TraceFileSystemContentStoreDiagnosticMessages { get; set; } = false;

        [DataMember]
        public bool TraceProactiveCopy { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SilentOperationDurationThreshold { get; set; }

        [DataMember]
        public bool UseFastHibernationPin { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MaximumConcurrentPutAndPlaceFileOperations { get; set; }

        [DataMember]
        public bool EnableMetadataStore { get; set; } = false;

        [DataMember]
        public bool EnableDistributedCache { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int MaximumNumberOfMetadataEntriesToStore { get; set; } = 500_000;

        [DataMember]
        public bool UseRedisMetadataStore { get; set; } = false;

        /// <summary>
        /// Gets or sets the time period between logging incremental stats
        /// </summary>
        [DataMember]
        public TimeSpan? LogIncrementalStatsInterval { get; set; }

        /// <summary>
        /// Gets or sets the time period between logging machine-specific performance statistics.
        /// </summary>
        [DataMember]
        public TimeSpan? LogMachineStatsInterval { get; set; }

        #endregion

        #region Proactive Copy / Replication

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int MaxConcurrentProactiveCopyOperations { get; set; } = DefaultMaxConcurrentCopyOperations;

        /// <summary>
        /// Valid values: Disabled, InsideRing, OutsideRing, Both (See ProactiveCopyMode enum)
        /// </summary>
        [DataMember]
        [Validation.Enum(typeof(ProactiveCopyMode))]
        public string ProactiveCopyMode { get; set; } = "Disabled";

        [DataMember]
        public bool PushProactiveCopies { get; set; } = false;

        [DataMember]
        public bool ProactiveCopyOnPut { get; set; } = true;

        [DataMember]
        public bool ProactiveCopyOnPin { get; set; } = false;

        [DataMember]
        public bool ProactiveCopyUsePreferredLocations { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ProactiveCopyLocationsThreshold { get; set; } = 3;

        [DataMember]
        public bool ProactiveCopyRejectOldContent { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int ProactiveReplicationCopyLimit { get; set; } = 5;

        [DataMember]
        public bool EnableProactiveReplication { get; set; } = false;

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double ProactiveReplicationDelaySeconds { get; set; } = 30;

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int PreferredLocationsExpiryTimeMinutes { get; set; } = 30;

        [DataMember]
        public bool UseBinManager { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public double TimeoutForProactiveCopiesMinutes { get; set; } = 15;

        #endregion
        
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
