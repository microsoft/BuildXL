// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

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

        public static DistributedContentSettings CreateDisabled()
        {
            return new DistributedContentSettings
            {
                IsDistributedContentEnabled = false,
            };
        }

        public static DistributedContentSettings CreateEnabled()
        {
            return new DistributedContentSettings()
            {
                IsDistributedContentEnabled = true,
            };
        }

        /// <summary>
        /// Settings for running deployment launcher
        /// </summary>
        [DataMember]
        public LauncherSettings LauncherSettings { get; set; } = null;

        /// <summary>
        /// Feature flag to turn on distributed content tracking (L2/datacenter cache).
        /// </summary>
        [DataMember]
        public bool IsDistributedContentEnabled { get; set; }

        /// <summary>
        /// Grpc port for backing cache service instance
        /// </summary>
        [DataMember]
        public int? BackingGrpcPort { get; set; } = null;

        /// <summary>
        /// Grpc port for backing cache service instance
        /// </summary>
        [DataMember]
        public string BackingScenario { get; set; } = null;

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

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int ProactiveCopyMaxRetries { get; set; } = 0;

        /// <summary>
        /// Configurable Keyspace Prefixes
        /// </summary>
        [DataMember]
        public string KeySpacePrefix { get; set; } = "CBPrefix";

        /// <summary>
        /// Name of the EventHub instance to connect to.
        /// </summary>
        [DataMember]
        public string EventHubName { get; set; } = "eventhub";

        /// <summary>
        /// Name of the EventHub instance's consumer group name.
        /// </summary>
        [DataMember]
        public string EventHubConsumerGroupName { get; set; } = "$Default";

        /// <summary>
        /// Adds suffix to resource names to allow instances in different ring to
        /// share the same resource without conflicts. In particular this will add the
        /// ring suffix to Azure blob container name and Redis keyspace (which is included
        /// in event hub epoch). 
        /// </summary>
        [DataMember]
        public bool UseRingIsolation { get; set; } = false;

        // Redis-related configuration

        /// <summary>
        /// TTL to be set in Redis for memoization entries.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int RedisMemoizationExpiryTimeMinutes { get; set; } = 1500;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int RedisBatchPageSize { get; set; } = 500;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RedisConnectionErrorLimit { get; set; }

        [DataMember]
        public bool? UseRedisPreventThreadTheftFeature { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RedisMemoizationDatabaseOperationTimeoutInSeconds { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? RedisReconnectionLimitBeforeServiceRestart { get; set; }

        [DataMember]
        public bool? TraceRedisFailures { get; set; }

        [DataMember]
        public bool? TraceRedisTransientFailures { get; set; }

        [DataMember]
        public double? DefaultRedisOperationTimeoutInSeconds { get; set; }

        [DataMember]
        public TimeSpan? MinRedisReconnectInterval { get; set; }

        [DataMember]
        public bool? CancelBatchWhenMultiplexerIsClosed { get; set; }

        [DataMember]
        public bool? TreatObjectDisposedExceptionAsTransient { get; set; }

        [DataMember]
        [Validation.Range(-1, int.MaxValue)]
        public int? RedisGetBlobTimeoutMilliseconds { get; set; }

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int? RedisGetCheckpointStateTimeoutInSeconds { get; set; }

        // Redis retry configuration
        [DataMember]
        [Validation.Range(0, int.MaxValue, minInclusive: false)]
        public int? RedisFixedIntervalRetryCount { get; set; }

        // Just setting this value is enough to configure a custom exponential back-off policy with default min/max/interval.
        [DataMember]
        [Validation.Range(0, int.MaxValue, minInclusive: false)]
        public int? RedisExponentialBackoffRetryCount { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RedisExponentialBackoffMinIntervalInSeconds { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RedisExponentialBackoffMaxIntervalInSeconds { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RedisExponentialBackoffDeltaIntervalInSeconds { get; set; }


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
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? ReserveSpaceTimeoutInMinutes { get; set; }

        [DataMember]
        public bool IsRepairHandlingEnabled { get; set; } = false;

        [DataMember]
        public bool UseMdmCounters { get; set; } = true;

        [DataMember]
        public bool UseContextualEntryDatabaseOperationLogging { get; set; } = false;

        [DataMember]
        public bool TraceTouches { get; set; } = true;

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

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double PeriodicCopyTracingIntervalMinutes { get; set; } = 5.0;

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

        [DataMember]
        public bool UseUnsafeByteStringConstruction { get; set; } = false;

        #region Grpc File Copier

        [DataMember]
        public string GrpcFileCopierGrpcCopyClientInvalidationPolicy { get; set; }

        #endregion

        #region Grpc Copy Client Cache

        [DataMember]
        [Validation.Range(0, 2)]
        public int? GrpcCopyClientCacheResourcePoolVersion { get; set; }

        /// <summary>
        /// Upper bound on number of cached GRPC clients.
        /// </summary>
        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? MaxGrpcClientCount { get; set; }

        /// <summary>
        /// Maximum cached age for GRPC clients.
        /// </summary>
        [DataMember]
        [Validation.Range(1, double.MaxValue)]
        public double? MaxGrpcClientAgeMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, double.MaxValue)]
        public double? GrpcCopyClientCacheGarbageCollectionPeriodMinutes { get; set; }

        [DataMember]
        public bool? GrpcCopyClientCacheEnableInstanceInvalidation { get; set; }

        #endregion

        #region Grpc Copy Client

        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public int? GrpcCopyClientBufferSizeBytes { get; set; }

        [DataMember]
        public bool? GrpcCopyClientUseGzipCompression { get; set; }

        [DataMember]
        public bool? GrpcCopyClientConnectOnStartup { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double? GrpcCopyClientConnectionEstablishmentTimeoutSeconds { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double? GrpcCopyClientDisconnectionTimeoutSeconds { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue)]
        public double? GrpcCopyClientConnectionTimeoutSeconds { get; set; }

        #endregion

        #region Distributed Eviction

        /// <summary>
        /// When set to true, we will shut down the quota keeper before hibernating sessions to prevent a race condition of evicting pinned content
        /// </summary>
        [DataMember]
        public bool ShutdownEvictionBeforeHibernation { get; set; } = false;

        [DataMember]
        public bool IsDistributedEvictionEnabled { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ReplicaCreditInMinutes { get; set; } = 180;

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

        [DataMember]
        public BandwidthConfiguration[] BandwidthConfigurations { get; set; }
        #endregion

        #region Pin Better
        [DataMember]
        public bool IsPinBetterEnabled { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? PinMinUnverifiedCount { get; set; }

        /// <summary>
        /// Obsolete: will be removed in favor of AsyncCopyOnPinThreshold.
        /// </summary>
        [DataMember]
        public int? StartCopyWhenPinMinUnverifiedCountThreshold { get; set; }

        [DataMember]
        public int? AsyncCopyOnPinThreshold { get; set; }

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

        [DataMember]
        public bool? DistributedCentralStorageImmutabilityOptimizations { get; set; }

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
        public bool ContentLocationDatabaseLogsBackupEnabled { get; set; }

        [DataMember]
        public bool? ContentLocationDatabaseOpenReadOnly { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? ContentLocationDatabaseLogsBackupRetentionMinutes { get; set; }

        /// <remarks>
        /// 0 means infinite here (i.e. there won't be any compactions)
        /// </remarks>
        [DataMember]
        [Validation.Range(0, int.MaxValue)]
        public double? FullRangeCompactionIntervalMinutes { get; set; }

        [DataMember]
        public string FullRangeCompactionVariant { get; set; }

        [DataMember]
        [Validation.Range(1, byte.MaxValue)]
        public byte? FullRangeCompactionByteIncrementStep { get; set; }

        [DataMember]
        public bool? ContentLocationDatabaseEnableDynamicLevelTargetSizes { get; set; }

        [DataMember]
        [Validation.Range(1, long.MaxValue)]
        public long? ContentLocationDatabaseEnumerateSortedKeysFromStorageBufferSize { get; set; }

        [DataMember]
        [Validation.Range(1, long.MaxValue)]
        public long? ContentLocationDatabaseEnumerateEntriesWithSortedKeysFromStorageBufferSize { get; set; }

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
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? RestoreCheckpointTimeoutMinutes { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? UpdateClusterStateIntervalSeconds { get; set; }

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
        public bool? PacemakerEnabled { get; set; }

        [DataMember]
        public uint? PacemakerNumberOfBuckets { get; set; }

        [DataMember]
        public bool? PacemakerUseRandomIdentifier { get; set; }

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
        public bool? UseAsynchronousFileStreamOptionByDefault { get; set; }

        [DataMember]
        public bool TraceProactiveCopy { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SilentOperationDurationThreshold { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? DefaultPendingOperationTracingIntervalInMinutes { get; set; }

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

        [DataMember]
        public bool UseRoxisMetadataStore { get; set; } = false;

        [DataMember]
        public string RoxisMetadataStoreHost { get; set; } = null;

        [DataMember]
        public int? RoxisMetadataStorePort { get; set; } = null;

        /// <summary>
        /// Gets or sets the time period between logging incremental stats
        /// </summary>
        [DataMember]
        public TimeSpan? LogIncrementalStatsInterval { get; set; }

        [DataMember]
        public string[] IncrementalStatisticsCounterNames { get; set; }

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

        [DataMember]
        [Validation.Enum(typeof(MultiplexMode))]
        public string MultiplexStoreMode { get; set; } = nameof(MultiplexMode.Legacy);

        public MultiplexMode GetMultiplexMode()
        {
            return (MultiplexMode)Enum.Parse(typeof(MultiplexMode), MultiplexStoreMode);
        }

        #endregion        
        /// <summary>
        /// The map of drive paths to alternate paths to access them
        /// </summary>
        [DataMember]
        private IDictionary<string, string> AlternateDriveMap { get; set; }

        [DataMember]
        public bool TouchContentHashLists { get; set; }

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

    internal class JsonStringEnumConverter
    {
    }

    /// <summary>
    /// Specifies which multiplexing cache topology to use multiplex between drives.
    /// </summary>
    public enum MultiplexMode
    {
        /// <summary>
        /// Defines the legacy multiplexing mode with a single multiplexed store at the root
        /// 
        ///     MultiplexedContentStore
        ///         DistributedContentStore (LLS Machine Location = D:\)
        ///             FileSystemContentStore (D:\)
        ///         DistributedContentStore (LLS Machine Location = K:\)
        ///             FileSystemContentStore (K:\)
        ///
        ///     LLS settings = (PrimaryMachineLocation = D:\, AdditionalMachineLocations = [K:\])
        /// </summary>
        Legacy,

        /// <summary>
        /// Defines the transitioning multiplexing mode with root distributed store nesting multiplexed file system stores
        /// but still maintaining LLS machine locations for all drives 
        ///
        ///     DistributedContentStore (LLS Machine Location = D:\)
        ///         MultiplexedContentStore
        ///             FileSystemContentStore (D:\)
        ///             FileSystemContentStore (K:\)
        ///
        ///     LLS settings = (PrimaryMachineLocation = D:\, AdditionalMachineLocations = [K:\])
        ///     
        ///     Keeps same LLS settings so it continues to heartbeat to keep K:\ (secondary) drive alive
        ///     During reconcile, content from K:\ drive will be added to D:\ (primary) machine location
        ///     NOTE: GRPC content server does not care or know about machine locations, it will try to retrieve content from all drives.
        /// </summary>
        Transitional,

        /// <summary>
        /// Defines the transitioning multiplexing mode with root distributed store nesting multiplexed file system stores
        /// with only a single lls machine location for the primary drive
        ///
        ///     DistributedContentStore (LLS Machine Location = D:\)
        ///         MultiplexedContentStore
        ///             FileSystemContentStore (D:\)
        ///             FileSystemContentStore (K:\)
        ///
        ///     LLS settings = (PrimaryMachineLocation = D:\, AdditionalMachineLocations = [])
        ///
        ///     Finally, LLS settings only mention the single primary (D:\) machine location, and all content for the machine (on all drives) is
        ///     now registered under that machine location after going through the Transitional state.
        ///     NOTE: GRPC content server does not care or know about machine locations, it will try to retrieve content from all drives.
        /// </summary>
        Unified
    }

    /// <nodoc />
    public class BandwidthConfiguration
    {
        /// <summary>
        /// Whether to invalidate Grpc Copy Client in case of an error.
        /// </summary>
        public bool? InvalidateOnTimeoutError { get; set; }

        /// <summary>
        /// Gets an optional connection timeout that can be used to reject the copy more aggressively during early copy attempts.
        /// </summary>
        public double? ConnectionTimeoutInSeconds { get; set; }

        /// <summary>
        /// The interval between the copy progress is checked.
        /// </summary>
        public double IntervalInSeconds { get; set; }

        /// <summary>
        /// The number of required bytes that should be copied within a given interval. Otherwise the copy would be canceled.
        /// </summary>
        public long RequiredBytes { get; set; }
    }
}
