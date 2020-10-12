// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Utilities.Collections;

#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// A service role in terms of state synchronization.
    /// </summary>
    public enum Role
    {
        /// <summary>
        /// A service may produce and consume state synchronization events.
        /// </summary>
        Master,

        /// <summary>
        /// A service may only produce but NOT consume state synchronization events.
        /// </summary>
        Worker,
    }

    /// <summary>
    /// Configuration properties for <see cref="LocalLocationStore"/>
    /// </summary>
    public class LocalLocationStoreConfiguration
    {
        /// <summary>
        /// Indicates whether LLS operates in read-only mode where no writes are performed
        ///
        /// In this mode, the machine does not register itself as a part of the distributed network and thus is not
        /// discoverable as a content replica for any content on the machine. This is useful for scenarios where distributed network
        /// partially composed of machines which are short-lived and thus should not participate in the distributed network for the
        /// sake of avoid churn. The machines can still pull content from other machines by querying LLS DB/Redis and getting replicas
        /// on machines which are long-lived (and this flag is false).
        /// </summary>
        public bool DistributedContentConsumerOnly { get; set; }

        /// <summary>
        /// The machine location for the primary CAS folder on the machine.
        /// NOTE: LLS database is assumed to be stored on the same disk as this CAS instance.
        /// </summary>
        public MachineLocation PrimaryMachineLocation { get; set; }

        /// <summary>
        /// The machine locations for other CAS folders on the machine (i.e. other than <see cref="PrimaryMachineLocation"/>)
        /// </summary>
        public MachineLocation[] AdditionalMachineLocations { get; set; } = CollectionUtilities.EmptyArray<MachineLocation>();

        /// <summary>
        /// The default for <see cref="LocationEntryExpiry"/>
        /// </summary>
        public static readonly TimeSpan DefaultLocationEntryExpiry  = TimeSpan.FromHours(2);

        /// <summary>
        /// Interval between cluster state recomputations.
        /// </summary>
        public TimeSpan MachineStateRecomputeInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// The TTL on entries in RedisGlobalStore
        /// NOTE: This is NOT the same as ContentHashBumpTime (the TTL for entries in RedisContentLocationStore)
        /// </summary>
        public TimeSpan LocationEntryExpiry { get; set; } = DefaultLocationEntryExpiry;

        /// <summary>
        /// Configuration object for a local content location database.
        /// </summary>
        public ContentLocationDatabaseConfiguration? Database { get; set; }

        /// <summary>
        /// Configuration object for a content location event store.
        /// </summary>
        public ContentLocationEventStoreConfiguration? EventStore { get; set; } = null;

        /// <summary>
        /// Configuration for NuCache checkpointing logic.
        /// </summary>
        public CheckpointConfiguration? Checkpoint { get; set; } = null;

        /// <summary>
        /// Configuration of the central store.
        /// </summary>
        public CentralStoreConfiguration? CentralStore { get; set; } = null;

        /// <summary>
        /// Configuration of the distributed central store
        /// </summary>
        public DistributedCentralStoreConfiguration? DistributedCentralStore { get; set; } = null;

        /// <summary>
        /// Gets the connection string used by the redis global store.
        /// </summary>
        public string? RedisGlobalStoreConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the flag to enable thread theft prevention feature.
        /// </summary>
        /// <remarks>
        /// See this article about thread theft: https://stackexchange.github.io/StackExchange.Redis/ThreadTheft
        /// Basically, this feature forces all the continuations running asynchronously, i.e. all the user code is detached from the redis library's code.
        /// </remarks>
        public bool UsePreventThreadTheftFeature { get; set; } = false;

        /// <summary>
        /// Gets the connection string used by the redis global store.
        /// </summary>
        public string? RedisGlobalStoreSecondaryConnectionString { get; set; }

        /// <summary>
        /// The Redis.StackExchange library performs its own internal logging. If the flag is not null, we pipe the
        /// library's logging out through our own logger, at the specified severity.
        /// </summary>
        public Severity? RedisInternalLogSeverity { get; set; }

        /// <summary>
        /// Configuration of reputation tracker.
        /// </summary>
        public MachineReputationTrackerConfiguration ReputationTrackerConfiguration { get; set; } = new MachineReputationTrackerConfiguration();

        /// <summary>
        /// Specifies whether tiered eviction comparison should be used when ordering content for eviction
        /// </summary>
        public bool UseTieredDistributedEviction { get; set; }

        /// <summary>
        /// Specifies the time period over which throttled eviction is used where last remaining replicas are
        /// only allowed to be deleted in a throttled fashion
        /// </summary>
        public TimeSpan ThrottledEvictionInterval { get; set; }

        /// <summary>
        /// The number of buckets to offset by for important replicas. This effectively makes important replicas look younger.
        /// </summary>
        public int ImportantReplicaBucketOffset { get; set; } = 2;

        /// <summary>
        /// Indicates whether important replicas should be preserved.
        /// </summary>
        public bool PreserveImportantReplicas { get; set; } = true;

        /// <summary>
        /// Age buckets for use with tiered eviction
        /// </summary>
        public IReadOnlyList<TimeSpan> AgeBuckets { get; set; } =
            new TimeSpan[]
            {
                // Roughly 30 min + 3h^i
                TimeSpan.FromMinutes(30),
                TimeSpan.FromHours(2),
                TimeSpan.FromHours(4),
                TimeSpan.FromHours(10),
                TimeSpan.FromDays(1),
                TimeSpan.FromDays(3),
                TimeSpan.FromDays(10),
                TimeSpan.FromDays(30),
            };

        /// <summary>
        /// Controls the desired number of replicas to retain.
        /// </summary>
        public int DesiredReplicaRetention { get; set; } = 3;

        /// <summary>
        /// Estimated decay time for content re-use.
        /// </summary>
        /// <remarks><para>This is used in the optimal distributed eviction algorithm.</para></remarks>
        public TimeSpan ContentLifetime { get; set; } = TimeSpan.FromDays(0.5);

        /// <summary>
        /// Estimated chance of a content not being available on a machine in the distributed pool.
        /// </summary>
        /// <remarks><para>This is used in the optimal distributed eviction algorithm.</para></remarks>
        public double MachineRisk { get; set; } = 0.1;

        /// <summary>
        /// The minimum age of content before it is eagerly touched.
        /// </summary>
        public TimeSpan TouchFrequency { get; set; } = TimeSpan.FromHours(2);

        /// <summary>
        /// The threshold of machine locations over which additions are not sent to the global store but instead.
        /// only sent to event store
        /// </summary>
        public int SafeToLazilyUpdateMachineCountThreshold { get; set; } = 8;

        /// <summary>
        /// Indicates if redundant registrations of a content locations to be sent (i.e. location is already present in local db).
        /// </summary>
        public bool SkipRedundantContentLocationAdd { get; set; } = true;

        /// <summary>
        /// Indicates whether content is reconciled between local machine and local db once a checkpoint is restored.
        /// </summary>
        /// <remarks>
        /// Reconciliation is a very critical feature and disabling it can cause build failures because machine's state can be out of sync with LLS's data.
        /// </remarks>
        public bool EnableReconciliation { get; set; } = true;

        /// <summary>
        /// Indicates whether post-initialization steps (like reconciliation or processing state from the central store) are inlined during initialization. If false, operation is executed asynchronously and not awaited.
        /// </summary>
        /// <remarks>
        /// True only for tests.
        /// </remarks>
        public bool InlinePostInitialization { get; set; }

        /// <summary>
        /// The frequency by which reconciliation cycles should be done.
        /// </summary>
        public TimeSpan ReconciliationCycleFrequency { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Allows skipping reconciliation if the latest reconcile is sufficiently recent.
        /// </summary>
        public bool AllowSkipReconciliation { get; set; } = true;

        /// <summary>
        /// Diagnostic purposes only (extremely verbose). Gets or sets whether to log hashes which are added/removed in reconcile events.
        /// </summary>
        public bool LogReconciliationHashes { get; set; } = false;

        /// <summary>
        /// The amount of events that should be sent per reconciliation cycle.
        /// </summary>
        public int ReconciliationMaxCycleSize { get; set; } = 100000;

        /// <summary>
        /// The amount of events that should be sent per reconciliation cycle if the reconciliation cycle consists only
        /// of at most <see cref="ReconciliationMaxRemoveHashesAddPercentage"/>
        /// </summary>
        public int? ReconciliationMaxRemoveHashesCycleSize { get; set; } = null;

        /// <summary>
        /// Maximum percentage of adds in a reconciliation cycle to use
        /// <see cref="ReconciliationMaxRemoveHashesCycleSize"/> as the batch size.
        ///
        /// Defaults to 0 if <see cref="ReconciliationMaxRemoveHashesCycleSize"/> is enabled but this isn't
        /// </summary>
        public double? ReconciliationMaxRemoveHashesAddPercentage { get; set; } = null;

        /// <summary>
        /// Threshold under which proactive replication will be activated.
        /// </summary>
        public int ProactiveCopyLocationsThreshold { get; set; } = 3;

        /// <summary>
        /// Expiry time for preferred locations after being replaced or removed.
        /// </summary>
        public TimeSpan PreferredLocationsExpiryTime { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Whether to initialize and use the BinManager
        /// </summary>
        public bool UseBinManager { get; set; } = false;

        /// <summary>
        /// Indicates whether full sort algorithm should be used for getting hashes in eviction order
        /// </summary>
        public bool UseFullEvictionSort { get; set; } = false;

        /// <summary>
        /// Indicates whether local last access times should be updated if out of date with respect to distributed last access times
        /// </summary>
        public bool UpdateStaleLocalLastAccessTimes { get; set; } = false;

        /// <summary>
        /// Amount of entries to compute evictability metric for in a single pass. The larger this is, the faster the
        /// candidate pool fills up, but also the slower it is to produce a candidate. Helps control how fast we need
        /// to produce candidates.
        /// </summary>
        public int EvictionWindowSize { get; set; } = 500;

        /// <summary>
        /// Amount of entries to compute evictability metric for before determining eviction order. The larger this is,
        /// the slower and more resources eviction takes, but also the more accurate it becomes.
        /// </summary>
        /// <remarks>
        /// Two pools are kept in memory at the same time, so we effectively keep double the amount of data in memory.
        /// </remarks>
        public int EvictionPoolSize { get; set; } = 5000;

        /// <summary>
        /// Fraction of the pool considered trusted to be in the accurate order.
        /// </summary>
        /// <remarks>
        /// Estimated by looking into the percentage of files we remove of the total content store. Means we remove
        /// at most 76 entries per iteration when we stabilize.
        /// </remarks>
        public float EvictionRemovalFraction { get; set; } = 0.015355f;

        /// <summary>
        /// Fraction of the pool that can be trusted to be spurious at each iteration
        /// </summary>
        public float EvictionDiscardFraction { get; set; } = 0;

        /// <summary>
        /// The minimum age a candidate for eviction must be older than to be evicted. If the candidate's age is not older
        /// then we simply ignore it for eviction and trace information to help us determine why the candidate is nominated for eviction
        /// with such a younge age.
        /// <remarks>
        /// Default to zero time to allow all candidates to pass, when we want to test for eviction min age we can configure for it.
        /// </remarks>
        /// </summary>
        public TimeSpan EvictionMinAge { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Time Delay given to raided redis databases to complete its result after the first redis instance has completed.
        /// <remarks>
        /// Default value will be set to null, and both redis instances need to be completed before moving forward.
        /// </remarks>
        /// </summary>
        public TimeSpan? RetryWindow { get; set; }

        /// <summary>
        /// For testing purposes only. This is used for testing to prevent machine registration when
        /// using from production resources. Primary usage is for consuming checkpoints from production.
        /// </summary>
        public bool DisallowRegisterMachine { get; set; }

        /// <summary>
        /// Gets prefix used for checkpoints key which uniquely identifies a checkpoint lineage (i.e. changing this value indicates
        /// all prior checkpoints/cluster state are discarded and a new set of checkpoints is created)
        /// </summary>
        internal string? GetCheckpointPrefix() => CentralStore?.CentralStateKeyBase + EventStore?.Epoch;

        /// <summary>
        /// Whether to randomize elements in machine list, while still respecting reputation and other prioritizations.
        /// </summary>
        public bool RandomizeMachineList { get; set; }

        /// <summary>
        /// Whether to prioritize designated locations in machine lists, so that they are the first elements.
        /// </summary>
        public bool MachineListPrioritizeDesignatedLocations { get; set; }

        /// <summary>
        /// Whether to deprioritize the master in machine lists, so that it is the last element.
        /// </summary>
        public bool MachineListDeprioritizeMaster { get; set; }

        /// <summary>
        /// Indicates whether content hash lists should be touched after retrieval
        /// </summary>
        public bool TouchContentHashLists { get; set; }

        /// <summary>
        /// If this unsafe option is true, then the system works in a special benchmark-like mode:
        /// i.e. the instance will process the events but it want create the checkpoints.
        /// </summary>
        public bool MasterThroughputCheckMode { get; set; } = false;

        /// <summary>
        /// The start cursor position in Event Hub stream.
        /// </summary>
        public DateTime? EventHubCursorPosition { get; set; }

        /// <summary>
        /// Whether to create a separate Redis connection for blob operations.
        /// </summary>
        public bool UseSeparateConnectionForRedisBlobs { get; set; } = false;
    }

    /// <summary>
    /// Base class for a central store configuration.
    /// </summary>
    public class CentralStoreConfiguration
    {
        /// <summary>
        /// The key used to store checkpoints information in Redis.
        /// </summary>
        public string CentralStateKeyBase { get; }

        /// <nodoc />
        public CentralStoreConfiguration(string checkpointsKey) => CentralStateKeyBase = checkpointsKey;
    }

    /// <summary>
    /// Configuration for <see cref="DistributedCentralStorage"/>
    /// </summary>
    public class DistributedCentralStoreConfiguration
    {
        /// <nodoc />
        public DistributedCentralStoreConfiguration(AbsolutePath cacheRoot) => CacheRoot = cacheRoot;

        /// <summary>
        /// The working directory used by central store for storing 'uploaded' checkpoints.
        /// </summary>
        public AbsolutePath CacheRoot { get; }

        /// <summary>
        /// The time between iterations to wait for content to propagate to more machines
        /// </summary>
        public TimeSpan PropagationDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The number of retries to wait for content to propagate to enough machines to enable copying
        /// </summary>
        public int PropagationIterations { get; set; } = 3;

        /// <summary>
        /// See <see cref="BuildXL.Cache.ContentStore.Stores.ContentStoreSettings.TraceFileSystemContentStoreDiagnosticMessages"/>
        /// </summary>
        public bool TraceFileSystemContentStoreDiagnosticMessages = false;

        /// <summary>
        /// The maximum number of gigabytes to retain in CAS
        /// </summary>
        public int MaxRetentionGb { get; set; } = 20;

        /// <summary>
        /// Defines the target maximum number of simultaneous copies
        /// </summary>
        public int MaxSimultaneousCopies { get; set; } = 10;

        /// <summary>
        /// Maximum time to wait for a P2P copy to finish. When this times out, we will attempt to copy from the
        /// fallback storage.
        /// </summary>
        public TimeSpan PeerToPeerCopyTimeout { get; set; } = Timeout.InfiniteTimeSpan;

        /// <summary>
        /// Optional settings for validating CAS consistency used by DistributedCentralStorage.
        /// </summary>
        public SelfCheckSettings? SelfCheckSettings { get; set; }

        /// <summary>
        /// When enabled, we will use our knowledge of which checkpointed files are immutable or not to optimize copy
        /// operations into hardlinks.
        /// </summary>
        public bool ImmutabilityOptimizations { get; set; }
    }

    /// <summary>
    /// Configuration of a central store backed by azure blob storage.
    /// </summary>
    public class BlobCentralStoreConfiguration : CentralStoreConfiguration
    {
        /// <nodoc />
        public BlobCentralStoreConfiguration(IReadOnlyList<AzureBlobStorageCredentials> credentials, string containerName, string checkpointsKey)
            : base(checkpointsKey)
        {
            Contract.Requires(!string.IsNullOrEmpty(containerName));
            Contract.Requires(!string.IsNullOrEmpty(checkpointsKey));
            Contract.Requires(credentials != null && credentials.Count > 0, "BlobCentralStorage must have at least one set of credentials in its configuration.");

            ContainerName = containerName;
            Credentials = credentials;
        }

        /// <nodoc />
        public BlobCentralStoreConfiguration(AzureBlobStorageCredentials credentials, string containerName, string checkpointsKey)
            : this(new[] { credentials }, containerName, checkpointsKey)
        {
        }

        /// <summary>
        /// List of connection strings.
        /// </summary>
        public IReadOnlyList<AzureBlobStorageCredentials> Credentials { get; }

        /// <summary>
        /// The blob container name used to store checkpoints.
        /// </summary>
        public string ContainerName { get; }

        /// <summary>
        /// The retention time for checkpoint blobs.
        /// </summary>
        public TimeSpan RetentionTime { get; set; } = TimeSpan.FromHours(5);

        /// <summary>
        /// Indicates whether garbage collection of blobs is triggered after <see cref="RetentionTime"/>
        /// </summary>
        public bool EnableGarbageCollect { get; set; } = true;

        /// <nodoc />
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// Configuration of a central store backed by a local file system.
    /// </summary>
    public class LocalDiskCentralStoreConfiguration : CentralStoreConfiguration
    {
        /// <summary>
        /// The working directory used by central store for storing 'uploaded' checkpoints.
        /// </summary>
        public AbsolutePath WorkingDirectory { get; }

        /// <nodoc />
        public LocalDiskCentralStoreConfiguration(AbsolutePath workingDirectory, string checkpointsKey)
            : base(checkpointsKey) => WorkingDirectory = workingDirectory;
    }

    /// <summary>
    /// Configuration used by for creating/restoring checkpoints.
    /// </summary>
    public class CheckpointConfiguration
    {
        /// <summary>
        /// Temporary configuration of a machine's role.
        /// </summary>
        public Role? Role { get; set; } = Distributed.Role.Worker;

        /// <summary>
        /// The working directory used by checkpoint manager for staging checkpoints before upload and restore.
        /// </summary>
        public AbsolutePath WorkingDirectory { get; }

        /// <summary>
        /// Indicates whether incremental checkpointing is used
        /// </summary>
        public bool UseIncrementalCheckpointing { get; set; }

        /// <nodoc />
        public int IncrementalCheckpointDegreeOfParallelism { get; set; } = 1;

        /// <summary>
        /// The working directory used by checkpoint manager for staging checkpoints before upload and restore.
        /// </summary>
        /// <remarks>
        /// If null then checkpointing is disabled.
        /// </remarks>
        public AbsolutePath CheckpointWorkingDirectory => WorkingDirectory / "checkpoints";

        /// <summary>
        /// The time period before the master lease expires and is eligible to be taken by another machine.
        /// </summary>
        public TimeSpan MasterLeaseExpiryTime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The interval for heartbeats.
        /// </summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// The interval by which the checkpoint manager creates checkpoints.
        /// </summary>
        public TimeSpan CreateCheckpointInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The interval by which the checkpoint manager applies checkpoints to the local database.
        /// </summary>
        public TimeSpan RestoreCheckpointInterval { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Age threshold after which we should eagerly restore checkpoint blocking the caller.
        /// </summary>
        public TimeSpan RestoreCheckpointAgeThreshold { get; set; }

        /// <summary>
        /// Time after which a restore checkpoint operation is automatically cancelled.
        /// </summary>
        public TimeSpan? RestoreCheckpointTimeout { get; set; }

        /// <summary>
        /// The interval by which LLS' heartbeat will update the cluster state. Default is to do it on every heartbeat.
        /// </summary>
        public TimeSpan? UpdateClusterStateInterval { get; set; }

        /// <summary>
        /// Whether to enable the ability of machines to restore at a given interval.
        /// </summary>
        public bool PacemakerEnabled { get; set; } = false;

        /// <summary>
        /// Exact number of buckets to use. Allows us to skew the distribution.
        /// </summary>
        public uint? PacemakerNumberOfBuckets { get; set; } = null;

        /// <summary>
        /// Whether to use a random identifier at every heartbeat, or to use a deterministic identifier per checkpoint.
        /// </summary>
        public bool PacemakerUseRandomIdentifier { get; set; } = false;

        /// <inheritdoc />
        public CheckpointConfiguration(AbsolutePath workingDirectory) => WorkingDirectory = workingDirectory;
    }
}
