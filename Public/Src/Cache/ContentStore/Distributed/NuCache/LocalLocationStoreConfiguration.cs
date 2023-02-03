// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Host.Configuration;
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
    public record LocalLocationStoreConfiguration
    {
        /// <summary>
        /// The keyspace under which all keys in global store are stored
        /// </summary>
        public string? Keyspace { get; set; }

        /// <summary>
        /// The time before a machine is marked as closed from its last heartbeat as open.
        /// </summary>
        public TimeSpan MachineActiveToClosedInterval { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// The time before machines are marked as expired and locations are eligible for garbage collection from the local database
        /// </summary>
        public TimeSpan MachineActiveToExpiredInterval { get; set; } = TimeSpan.FromHours(1);

        /// <nodoc />
        public MetadataStoreMemoizationDatabaseConfiguration MetadataStoreMemoization { get; set; } = new MetadataStoreMemoizationDatabaseConfiguration();

        /// <nodoc />
        internal IClientAccessor<MachineLocation, IGlobalCacheService>? GlobalCacheClientAccessorForTests { get; set; }

        /// <summary>
        /// Indicates whether LLS operates in read-only mode where no writes are performed
        ///
        /// In this mode, the machine does not register itself as a part of the distributed network and thus is not
        /// discoverable as a content replica for any content on the machine. This is useful for scenarios where distributed network
        /// partially composed of machines which are short-lived and thus should not participate in the distributed network for the
        /// sake of avoid churn. The machines can still pull content from other machines by querying LLS DB/Global and getting replicas
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
        /// The TTL on entries in a global store
        /// NOTE: This is NOT the same as ContentHashBumpTime (the TTL for entries in global store)
        /// </summary>
        public TimeSpan LocationEntryExpiry { get; set; } = DefaultLocationEntryExpiry;

        /// <summary>
        /// Configuration object for a local content location database.
        /// </summary>
        public ContentLocationDatabaseConfiguration? Database { get; set; }

        /// <summary>
        /// Configuration object for the content metadata store
        /// </summary>
        public ClientContentMetadataStoreConfiguration? MetadataStore { get; set; }

        /// <summary>
        /// A helper method that provides extra information to the compiler regarding whether some properties are null or not.
        /// </summary>
        [MemberNotNullWhen(true, nameof(Database))]
        [MemberNotNullWhen(true, nameof(EventStore))]
        [MemberNotNullWhen(true, nameof(Checkpoint))]
        [MemberNotNullWhen(true, nameof(CentralStore))]
        public bool IsValidForLls()
        {
            Contract.Assert(Database != null, "Database must be provided.");
            Contract.Assert(EventStore != null, "Event store must be provided.");
            Contract.Assert(Checkpoint != null, "Checkpointing must be provided.");
            Contract.Assert(CentralStore != null, "Central store must be provided.");
            return true;
        }

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

        /// <nodoc />
        public AzureBlobStorageCheckpointRegistryConfiguration? AzureBlobStorageCheckpointRegistryConfiguration { get; set; } = null;

        /// <nodoc />
        public AzureBlobStorageMasterElectionMechanismConfiguration? AzureBlobStorageMasterElectionMechanismConfiguration { get; set; } = null;

        /// <nodoc />
        public BlobClusterStateStorageConfiguration? BlobClusterStateStorageConfiguration { get; set; } = null;

        /// <nodoc />
        public ObservableMasterElectionMechanismConfiguration ObservableMasterElectionMechanismConfiguration { get; } = new ObservableMasterElectionMechanismConfiguration();

        /// <summary>
        /// Configuration of reputation tracker.
        /// </summary>
        public MachineReputationTrackerConfiguration ReputationTrackerConfiguration { get; set; } = new MachineReputationTrackerConfiguration();

        /// <summary>
        /// Specifies whether tiered eviction comparison should be used when ordering content for eviction
        /// </summary>
        public bool UseTieredDistributedEviction { get; set; }

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
        /// Amount of time we mark sent reconcile events as recent, and prevent sending them again.
        /// </summary>
        public TimeSpan ReconcileCacheLifetime { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// We check whether max processing delay is below this limit to call reconcile per checkpoint
        /// If not set, we do not check processing delay before reconciling
        /// </summary>
        public TimeSpan? MaxProcessingDelayToReconcile { get; set; } = null;

        /// <summary>
        /// The threshold of machine locations over which additions are not sent to the global store but instead.
        /// only sent to event store
        /// </summary>
        public int SafeToLazilyUpdateMachineCountThreshold { get; set; } = 8;

        /// <summary>
        /// Indicates whether content is reconciled between local machine and local db once a checkpoint is restored.
        /// </summary>
        /// <remarks>
        /// Reconciliation is a very critical feature and disabling it can cause build failures because machine's state can be out of sync with LLS's data.
        /// </remarks>
        public ReconciliationMode ReconcileMode { get; set; } = ReconciliationMode.Checkpoint;

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
        /// Maximum adds for local content without a record in a reconciliation cycle when <see cref="ReconcileMode"/> is checkpoint mode
        /// </summary>
        public int? ReconciliationAddLimit { get; set; } = null;

        /// <summary>
        /// Maximum removes for records without local content in a reconciliation cycle when <see cref="ReconcileMode"/> is checkpoint mode
        /// </summary>
        public int? ReconciliationRemoveLimit { get; set; } = null;

        /// <summary>
        /// Limit to the number of reconciled hashes to be logged
        /// </summary>
        public int ReconcileHashesLogLimit { get; set; } = 0;

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
        /// with such a young age.
        /// <remarks>
        /// Default to zero time to allow all candidates to pass, when we want to test for eviction min age we can configure for it.
        /// </remarks>
        /// </summary>
        public TimeSpan EvictionMinAge { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Gets prefix used for checkpoints key which uniquely identifies a checkpoint lineage (i.e. changing this value indicates
        /// all prior checkpoints/cluster state are discarded and a new set of checkpoints is created)
        /// </summary>
        internal string? GetCheckpointPrefix() => CentralStore?.CentralStateKeyBase + EventStore?.Epoch;

        /// <summary>
        /// Whether to prioritize designated locations in machine lists, so that they are the first elements.
        /// </summary>
        public bool MachineListPrioritizeDesignatedLocations { get; set; }

        /// <summary>
        /// Whether to filter out inactive machines in <see cref="LocalLocationStore"/> or rely on the old behavior when the filtering was happening on the database level only.
        /// </summary>
        public bool ShouldFilterInactiveMachinesInLocalLocationStore { get; set; } = false;

        /// <summary>
        /// Whether to filter out inactive machines in <see cref="LocalLocationStore"/> obtained from the global store.
        /// </summary>
        public bool FilterInactiveMachinesForGlobalLocations { get; set; }

        /// <summary>
        /// Whether to trace inactive machine count that could be filtered out because they're inactive.
        /// </summary>
        /// <remarks>
        /// We observed the cases when the global information is incorrect and contains locations for inactive machines, but filtering out them
        /// unconditionally might be problematic because we could lose the data due to the lag in inactive -> active machine status updates.
        /// </remarks>
        public bool TraceInactiveMachinesForGlobalLocations { get; set; }

        public LocalLocationStoreSettings Settings { get; set; } = new();
    }

    /// <summary>
    /// Base class for a central store configuration.
    /// </summary>
    public abstract record CentralStoreConfiguration(string CentralStateKeyBase, string ContainerName)
    {
        public CentralStreamStorage CreateCentralStorage()
        {
            // TODO: Validate configuration before construction (bug 1365340)
            switch (this)
            {
                case LocalDiskCentralStoreConfiguration localDiskConfig:
                    return new LocalDiskCentralStorage(localDiskConfig);
                case BlobCentralStoreConfiguration blobStoreConfig:
                    return new BlobCentralStorage(blobStoreConfig);
                default:
                    throw new NotSupportedException();
            }
        }
    }

    /// <summary>
    /// Configuration for <see cref="DistributedCentralStorage"/>
    /// </summary>
    public record DistributedCentralStoreConfiguration(AbsolutePath CacheRoot)
    {
        /// <summary>
        /// The time between iterations to wait for content to propagate to more machines
        /// </summary>
        public TimeSpan PropagationDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The number of retries to wait for content to propagate to enough machines to enable copying
        /// </summary>
        public int PropagationIterations { get; set; } = 3;

        /// <summary>
        /// See <see cref="ContentStoreSettings.TraceFileSystemContentStoreDiagnosticMessages"/>
        /// </summary>
        public bool TraceFileSystemContentStoreDiagnosticMessages = false;

        /// <summary>
        /// The maximum number of gigabytes to retain in CAS
        /// </summary>
        public double MaxRetentionGb { get; set; } = 20;

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
        /// Whether to proactively copy checkpoint files to another machine.
        /// </summary>
        public bool ProactiveCopyCheckpointFiles { get; set; } = false;

        /// <summary>
        /// Whether to inline proactive copies done for checkpoint files or to do them asynchronously.
        /// </summary>
        public bool InlineCheckpointProactiveCopies { get; set; } = false;
    }

    /// <summary>
    /// Configuration of a central store backed by azure blob storage.
    /// </summary>
    public record BlobCentralStoreConfiguration : CentralStoreConfiguration
    {
        /// <nodoc />
        public BlobCentralStoreConfiguration(IReadOnlyList<AzureBlobStorageCredentials> credentials, string containerName, string checkpointsKey)
            : base(checkpointsKey, containerName)
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
    public record LocalDiskCentralStoreConfiguration : CentralStoreConfiguration
    {
        /// <summary>
        /// The working directory used by central store for storing 'uploaded' checkpoints.
        /// </summary>
        public AbsolutePath WorkingDirectory { get; }

        /// <nodoc />
        public LocalDiskCentralStoreConfiguration(AbsolutePath workingDirectory, string checkpointsKey)
            : base(checkpointsKey, ContainerName: "") => WorkingDirectory = workingDirectory;
    }

    public record CheckpointManagerConfiguration(AbsolutePath WorkingDirectory, MachineLocation PrimaryMachineLocation)
    {
        /// <summary>
        /// The interval by which the checkpoint manager creates checkpoints.
        /// </summary>
        public TimeSpan CreateCheckpointInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The interval by which the checkpoint manager applies checkpoints to the local database.
        /// </summary>
        public TimeSpan RestoreCheckpointInterval { get; set; } = TimeSpan.FromMinutes(10);

        /// <nodoc />
        public int IncrementalCheckpointDegreeOfParallelism { get; set; } = 1;

        /// <summary>
        /// Time after which a restore checkpoint operation is automatically cancelled.
        /// </summary>
        public TimeSpan RestoreCheckpointTimeout { get; set; } = TimeSpan.MaxValue;

        /// <summary>
        /// Gets whether the checkpoint manager has a timer loop to restore checkpoints. This is normally handled by
        /// the database owner (i.e. LLS or GCS). But is provided as an option for the case where GCS DB is synced to workers.
        /// </summary>
        public bool RestoreCheckpoints { get; set; }
    }

    /// <summary>
    /// Configuration used by for creating/restoring checkpoints.
    /// </summary>
    public record CheckpointConfiguration(AbsolutePath WorkingDirectory, MachineLocation PrimaryMachineLocation)
        : CheckpointManagerConfiguration(WorkingDirectory, PrimaryMachineLocation)
    {
        /// <summary>
        /// Temporary configuration of a machine's role.
        /// </summary>
        public Role? Role { get; set; } = Distributed.Role.Worker;

        /// <summary>
        /// The time period before the master lease expires and is eligible to be taken by another machine.
        /// </summary>
        public TimeSpan MasterLeaseExpiryTime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The interval for heartbeats.
        /// </summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Age threshold after which we should eagerly restore checkpoint blocking the caller.
        /// </summary>
        public TimeSpan RestoreCheckpointAgeThreshold { get; set; }

        /// <summary>
        /// The interval by which LLS' heartbeat will update the cluster state. Default is to do it on every heartbeat.
        /// </summary>
        public TimeSpan UpdateClusterStateInterval { get; set; } = TimeSpan.FromMinutes(1);
    }
}
