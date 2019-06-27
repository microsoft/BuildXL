// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;

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
    /// Defines a mode of <see cref="TransitioningContentLocationStore"/>.
    /// </summary>
    [Flags]
    public enum ContentLocationMode
    {
        /// <summary>
        /// Specifies that only <see cref="RedisContentLocationStore"/> be used.
        /// </summary>
        Redis = 1 << 0,

        /// <summary>
        /// Specifies that only <see cref="NuCache.LocalLocationStore"/> should be used
        /// </summary>
        LocalLocationStore = 1 << 1,

        /// <summary>
        /// Specifies that both stores should be used
        /// </summary>
        Both = Redis | LocalLocationStore
    }

    /// <summary>
    /// Configuration properties for <see cref="LocalLocationStore"/>
    /// </summary>
    public class LocalLocationStoreConfiguration
    {
        /// <summary>
        /// The default for <see cref="LocationEntryExpiry"/>
        /// </summary>
        public static readonly TimeSpan DefaultLocationEntryExpiry  = TimeSpan.FromHours(2);

        /// <summary>
        /// Interval between cluster state recomputations.
        /// </summary>
        public TimeSpan RecomputeInactiveMachinesExpiry { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// The TTL on entries in RedisGlobalStore
        /// NOTE: This is NOT the same as ContentHashBumpTime (the TTL for entries in RedisContentLocationStore)
        /// </summary>
        public TimeSpan LocationEntryExpiry { get; set; } = DefaultLocationEntryExpiry;

        /// <summary>
        /// Configuration object for a local content location database.
        /// </summary>
        public ContentLocationDatabaseConfiguration Database { get; set; }

        /// <summary>
        /// Configuration object for a content location event store.
        /// </summary>
        public ContentLocationEventStoreConfiguration EventStore { get; set; } = null;

        /// <summary>
        /// Configuration for NuCache checkpointing logic.
        /// </summary>
        public CheckpointConfiguration Checkpoint { get; set; } = null;

        /// <summary>
        /// Configuration of the central store.
        /// </summary>
        public CentralStoreConfiguration CentralStore { get; set; } = null;

        /// <summary>
        /// Configuration of the distributed central store
        /// </summary>
        public DistributedCentralStoreConfiguration DistributedCentralStore { get; set; } = null;

        /// <summary>
        /// Gets the connection string used by the redis global store.
        /// </summary>
        public string RedisGlobalStoreConnectionString { get; set; }

        /// <summary>
        /// Gets the connection string used by the redis global store.
        /// </summary>
        public string RedisGlobalStoreSecondaryConnectionString { get; set; }

        /// <summary>
        /// Configuration of reputation tracker.
        /// </summary>
        public MachineReputationTrackerConfiguration ReputationTrackerConfiguration { get; set; } = new MachineReputationTrackerConfiguration();

        /// <summary>
        /// Estimated decay time for content re-use.
        /// </summary>
        /// <remarks><para>This is used in the opitmal distributed eviction algorithm.</para></remarks>
        public TimeSpan ContentLifetime { get; set; } = TimeSpan.FromDays(0.5);

        /// <summary>
        /// Estimated chance of a content not being available on a machine in the distributed pool.
        /// </summary>
        /// <remarks><para>This is used in the opitmal distributed eviction algorithm.</para></remarks>
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
        public bool EnableReconciliation { get; set; } = true;

        /// <summary>
        /// Indicates whether post-initialization steps (like reconciliation or processing state from the central store) are inlined during initialization. If false, operation is executed asynchronously and not awaited.
        /// </summary>
        /// <remarks>
        /// True only for tests.
        /// </remarks>
        public bool InlinePostInitialization { get; set; }

        /// <summary>
        /// Gets prefix used for checkpoints key which uniquely identifies a checkpoint lineage (i.e. changing this value indicates
        /// all prior checkpoints/cluster state are discarded and a new set of checkpoints is created)
        /// </summary>
        internal string GetCheckpointPrefix() => CentralStore.CentralStateKeyBase + EventStore.Epoch;

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
        public DistributedCentralStoreConfiguration(AbsolutePath cacheRoot)
        {
            CacheRoot = cacheRoot;
        }

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
        /// The maximum number of gigabytes to retain in CAS
        /// </summary>
        public int MaxRetentionGb { get; set; } = 20;

        /// <summary>
        /// Defines the target maximum number of simulataneous copies
        /// </summary>
        public int MaxSimultaneousCopies { get; set; } = 10;
    }

    /// <summary>
    /// Provides Azure Storage authentication options for <see cref="BlobCentralStorage"/>
    /// </summary>
    public class AzureBlobStorageCredentials
    {
        /// <nodoc />
        private string ConnectionString { get; }

        /// <summary>
        /// <see cref="StorageCredentials"/> can be updated from the outside, so it is a way to in fact change the way
        /// we authenticate with Azure Blob Storage over time. Changes are accepted only within the same authentication
        /// mode.
        /// </summary>
        private StorageCredentials StorageCredentials { get; }

        /// <nodoc />
        private string AccountName { get; }

        /// <nodoc />
        private string EndpointSuffix { get; }

        /// <summary>
        /// Creates a fixed credential; this is our default mode of authentication.
        /// </summary>
        public AzureBlobStorageCredentials(string connectionString)
        {
            Contract.Requires(!string.IsNullOrEmpty(connectionString));
            ConnectionString = connectionString;
        }

        /// <summary>
        /// Uses Azure Blob's storage credentials. This allows us to use SAS tokens, and to update shared secrets
        /// without restarting the service.
        /// </summary>
        public AzureBlobStorageCredentials(StorageCredentials storageCredentials, string accountName, string endpointSuffix = null)
        {
            // Unfortunately, even though you can't generate a storage credentials without an account name, it isn't
            // stored inside object unless a shared secret is being used. Hence, we are forced to keep it here.
            Contract.Requires(storageCredentials != null);
            Contract.Requires(!string.IsNullOrEmpty(accountName));
            StorageCredentials = storageCredentials;
            AccountName = accountName;
            EndpointSuffix = endpointSuffix;
        }

        /// <nodoc />
        private CloudStorageAccount CreateCloudStorageAccount()
        {
            if (!string.IsNullOrEmpty(ConnectionString))
            {
                return CloudStorageAccount.Parse(ConnectionString);
            }

            if (StorageCredentials != null)
            {
                return new CloudStorageAccount(StorageCredentials, AccountName, EndpointSuffix, useHttps: true);
            }

            throw new ArgumentException("Invalid credentials");
        }

        /// <nodoc />
        public CloudBlobClient CreateCloudBlobClient()
        {
            return CreateCloudStorageAccount().CreateCloudBlobClient();
        }
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

        /// <inheritdoc />
        public CheckpointConfiguration(AbsolutePath workingDirectory) => WorkingDirectory = workingDirectory;
    }
}
