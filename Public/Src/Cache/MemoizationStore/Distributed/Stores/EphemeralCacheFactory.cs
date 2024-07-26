// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using ProtoBuf.Grpc.Client;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <nodoc />
public static class EphemeralCacheFactory
{
    /// <nodoc />
    public abstract record Configuration
    {
        /// <summary>
        /// Location on drive where cache is located.
        /// </summary>
        /// <remarks>
        /// Cache will take over this drive, and it's not share-able with other caches.
        /// </remarks>
        public required AbsolutePath RootPath { get; init; }

        /// <summary>
        /// Hostname and port that allows other machines to communicate with this machine
        /// </summary>
        /// <remarks>
        /// The suggested port to set this to is GrpcConstants.DefaultEphemeralGrpcPort. When using
        /// encrypted communication, the suggested port is GrpcConstants.DefaultEphemeralEncryptedGrpcPort.
        /// </remarks>
        public required MachineLocation Location { get; init; }

        /// <summary>
        /// Hostname and port of the leader machine
        /// </summary>
        /// <remarks>
        /// The suggested port to set this to is GrpcConstants.DefaultEphemeralLeaderGrpcPort. When using
        /// encrypted communication, the suggested port is GrpcConstants.DefaultEphemeralLeaderEncryptedGrpcPort.
        /// </remarks>
        public required MachineLocation Leader { get; init; }

        /// <summary>
        /// Maximum size of the cache.
        /// </summary>
        public required uint MaxCacheSizeMb { get; init; }

        /// <summary>
        /// Heartbeat interval.
        /// </summary>
        /// <remarks>
        /// This parameter controls how often we send a heartbeat to the cluster. The heartbeat is used to detect when
        /// a machine is offline.
        /// </remarks>
        public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Maximum time to wait to establish or finalize a P2P gRPC connection
        /// </summary>
        public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// Maximum time to wait for the result of a GetLocations gRPC request
        /// </summary>
        public TimeSpan GetLocationsTimeout { get; init; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Maximum time to wait for the result of a UpdateLocations gRPC request
        /// </summary>
        public TimeSpan UpdateLocationsTimeout { get; init; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// The maximum staleness we're willing to tolerate to elide a remote locations query when acting as a worker.
        /// </summary>
        public TimeSpan MaximumWorkerStaleness { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The maximum staleness we're willing to tolerate to elide a remote locations query when acting as a leader.
        /// </summary>
        public TimeSpan MaximumLeaderStaleness { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Inline change processing.
        /// 
        /// WARNING: TESTING ONLY.
        /// </summary>
        internal bool TestInlineChangeProcessing { get; set; } = false;
    }

    /// <summary>
    /// This configuration allows us to create a cache that is shared by all builds in the datacenter. This means that
    /// content generated by any of the concurrently running builds will be available to all other concurrently running
    /// builds.
    /// </summary>
    public sealed record DatacenterWideCacheConfiguration : Configuration
    {
        /// <summary>
        /// The default universe
        /// </summary>
        public static readonly string DefaultUniverse = "default";

        /// <summary>
        /// The universe allows caches with shared resources to split up into different logical caches.
        /// </summary>
        public required string Universe { get; init; } = DefaultUniverse;

        /// <summary>
        /// Maximum timeout for storage operations
        /// </summary>
        public TimeSpan StorageInteractionTimeout { get; set; }
    };

    /// <summary>
    /// This configuration allows us to create a cache that is shared only by the builders collaborating on a single
    /// build.
    /// </summary>
    public sealed record BuildWideCacheConfiguration : Configuration
    {
        /// <summary>
        /// Maximum time to wait to establish or finalize a gRPC connection to the leader for the purposes of tracking
        /// the cluster's state.
        /// </summary>
        /// <remarks>
        /// This defaults to infinite because it is assumed that a worker can start up an arbitrary amount of time
        /// before the leader does. This is because the processes are started up independently, so we let the build
        /// engine handle the synchronization here.
        /// </remarks>
        public TimeSpan ClusterStateConnectionTimeout { get; init; } = Timeout.InfiniteTimeSpan;
    };

    /// <nodoc />
    public record struct CreateResult
    {
        /// <nodoc />
        public IFullCache Cache { get; init; }

        /// <nodoc />
        internal EphemeralHost Host { get; init; }

        /// <nodoc />
        internal CreateResult(EphemeralHost host, IFullCache cache)
        {
            Host = host;
            Cache = cache;
        }
    }

    /// <summary>
    /// Creates a P2P cache.
    /// </summary>
    public static Task<CreateResult> CreateAsync(OperationContext context, Configuration configuration, AzureBlobStorageCacheFactory.CreateResult persistentCache, IClock? clock = null)
    {
        context.TracingContext.Warning($"Creating cache with BuildXL version {Branding.Version}", nameof(EphemeralCacheFactory));

        GrpcEnvironment.Initialize(context.TracingContext.Logger, new GrpcEnvironmentOptions()
        {
            LoggingVerbosity = GrpcEnvironmentOptions.GrpcVerbosity.Disabled,
        });

        clock ??= SystemClock.Instance;
        return configuration switch
        {
            DatacenterWideCacheConfiguration datacenterWideCacheConfiguration => CreateDatacenterWideCacheAsync(context, datacenterWideCacheConfiguration, persistentCache, clock),
            BuildWideCacheConfiguration buildWideCacheConfiguration => CreateBuildWideCacheAsync(context, buildWideCacheConfiguration, persistentCache, clock),
            _ => throw new NotSupportedException($"Cache type {configuration.GetType().Name} is not supported.")
        };
    }

    /// <summary>
    /// The value returned by this function is used to partition the cache into separate logical clusters.
    /// </summary>
    /// <remarks>
    /// We split the ephemeral cache on the following dimensions:
    ///  - Universe
    ///  - Namespace
    ///  - Assembly Version
    ///  - The persistent cache's sharding matrix (i.e., which specific storage accounts in a specific order and with a
    ///    given sharding matrix).
    ///
    /// This guarantees a series of properties that are very useful to have:
    /// 1. Splitting on a per-assembly basis simplifies deployment by allowing us to assume that the wire protocol and
    ///    data representations on a per-assembly basis and deployed atomically (i.e., there's no chance of having
    ///    backwards or forwards compatibility issues).
    /// 2. Splitting per universe and namespace guarantees that caches are isolated from each other in the same way the
    ///    underlying persistent cache does it. This prevents a number of possible bad scenarios from happening, such
    ///    as getting a file from a build you wouldn't normally have been able to get a cache hit from.
    /// 3. Splitting on the sharding matrix guarantees that changes in the sharding scheme look like separate caches,
    ///    which means there's no need to handle migration of data between sharding schemes in the ephemeral, even
    ///    though the persistent may need to.
    /// </remarks>
    private static string GenerateEphemeralPartitionId(DatacenterWideCacheConfiguration configuration, AzureBlobStorageCacheFactory.CreateResult persistentCache)
    {
        var universe = HashCodeHelper.GetOrdinalIgnoreCaseHashCode64(configuration.Universe);
        var @namespace = HashCodeHelper.GetOrdinalIgnoreCaseHashCode64(persistentCache.Configuration.Namespace);
        var version = HashCodeHelper.GetOrdinalIgnoreCaseHashCode64(Branding.Version);
        var (metadataSalt, contentSalt) = persistentCache.Configuration.ShardingScheme.GenerateSalt();
        var hash = HashCodeHelper.Combine(new[] { universe, @namespace, version, metadataSalt, contentSalt });
        return hash.ToString("X16");
    }

    private static async Task<CreateResult> CreateDatacenterWideCacheAsync(
        OperationContext context,
        DatacenterWideCacheConfiguration configuration,
        AzureBlobStorageCacheFactory.CreateResult persistentCache,
        IClock clock)
    {
        if (string.IsNullOrEmpty(configuration.Universe))
        {
            configuration = configuration with { Universe = DatacenterWideCacheConfiguration.DefaultUniverse };
        }

        var clusterStateContainer = persistentCache.Topology.EnumerateContainers(context, BlobCacheContainerPurpose.Checkpoint).First();
        var clusterStateCredentials = await persistentCache.SecretsProvider.RetrieveContainerCredentialsAsync(context, clusterStateContainer.Account, clusterStateContainer.Container);

        var blobClusterStateStorageConfiguration = new BlobClusterStateStorageConfiguration()
        {
            Storage = new BlobClusterStateStorageConfiguration.StorageSettings(clusterStateCredentials, ContainerName: clusterStateContainer.Container.ToString(), FolderName: "clusterState"),
            BlobFolderStorageConfiguration = new()
            {
                StorageInteractionTimeout = configuration.StorageInteractionTimeout,
            },
            // The following file splits up the Ephemeral cache into separate logical caches that can't communicate
            // with each other. See documentation on the function below for information.
            FileName = $"clusterState-{GenerateEphemeralPartitionId(configuration, persistentCache)}.json",
            RecomputeConfiguration = new ClusterStateRecomputeConfiguration(),
        };
        var clusterStateStorage = new BlobClusterStateStorage(blobClusterStateStorageConfiguration, clock);

        var masterElectionMechanism = CreateMasterElectionMechanism(configuration.Location, configuration.Leader);
        return await CreateInternalAsync(
            context,
            configuration,
            masterElectionMechanism,
            clusterStateStorage,
            persistentCache,
            grpcClusterStateEndpoint: null,
            clock: clock);
    }

    private static Task<CreateResult> CreateBuildWideCacheAsync(
        OperationContext context,
        BuildWideCacheConfiguration configuration,
        AzureBlobStorageCacheFactory.CreateResult persistentCache,
        IClock clock)
    {
        var masterElectionMechanism = CreateMasterElectionMechanism(configuration.Location, configuration.Leader);

        IGrpcServiceEndpoint? grpcClusterStateEndpoint = null;
        IClusterStateStorage clusterStateStorage;

        // The following if statement is used to ensure that the Master machine DOES NOT use gRPC for Cluster State.
        // We need this because Cluster State is used for machine discovery, and for complicated reasons the
        // initialization of the gRPC service happens AFTER the initialization of the Cluster State. Therefore, doing
        // things any other way will result in a failure.
        if (masterElectionMechanism.Role == Role.Master)
        {
            clusterStateStorage = new InMemoryClusterStateStorage();
            var service = new GrpcClusterStateStorageService(clusterStateStorage);
            grpcClusterStateEndpoint = new ProtobufNetGrpcServiceEndpoint<IGrpcClusterStateStorage, GrpcClusterStateStorageService>(nameof(GrpcClusterStateStorageService), service);
        }
        else
        {
            clusterStateStorage = new GrpcClusterStateStorageClient(
                configuration: new GrpcClusterStateStorageClient.Configuration(
                    TimeSpan.FromSeconds(30),
                    RetryPolicyConfiguration.Exponential(maximumRetryCount: 100)),
                accessor: new DelayedFixedClientAccessor<IGrpcClusterStateStorage>(
                    async () =>
                    {
                        var connectionHandle = new ConnectionHandle(
                            context,
                            configuration.Leader,
                            // Allow waiting for the leader to setup for up to 30m
                            connectionTimeout: configuration.ClusterStateConnectionTimeout);
                        await connectionHandle.StartupAsync(context).ThrowIfFailureAsync();

                        return connectionHandle.Channel.CreateGrpcService<IGrpcClusterStateStorage>(MetadataServiceSerializer.ClientFactory);
                    },
                    configuration.Leader
                ),
                clock);
        }

        return CreateInternalAsync(
            context,
            configuration,
            masterElectionMechanism,
            clusterStateStorage,
            persistentCache,
            grpcClusterStateEndpoint,
            clock);
    }

    private static IMasterElectionMechanism CreateMasterElectionMechanism(MachineLocation location, MachineLocation leader)
    {
        if (location.Equals(leader))
        {
            return new RiggedMasterElectionMechanism(location, Role.Master);
        }

        return new RiggedMasterElectionMechanism(leader, Role.Worker);
    }

    /// <nodoc />
    private static Task<CreateResult> CreateInternalAsync(
        OperationContext context,
        Configuration configuration,
        IMasterElectionMechanism masterElectionMechanism,
        IClusterStateStorage clusterStateStorage,
        AzureBlobStorageCacheFactory.CreateResult persistentCache,
        IGrpcServiceEndpoint? grpcClusterStateEndpoint,
        IClock clock)
    {
        var (address, port) = configuration.Location.ExtractHostPort();
        Contract.Requires(port is not null, $"Port missing from the configured reachable DNS name: {configuration.Location}");

        var localContentTracker = new LocalContentTracker();

        var persistentLocations = persistentCache.Topology.EnumerateContainers(context, BlobCacheContainerPurpose.Content)
            .Select(path => path.ToMachineLocation()).ToArray();
        var clusterStateManagerConfiguration = new ClusterStateManager.Configuration
        {
            PrimaryLocation = configuration.Location,
            UpdateInterval = configuration.HeartbeatInterval,
            PersistentLocations = persistentLocations,
        };

        // TODO: reconfigure configuration here and and above
        var clusterStateManager = new ClusterStateManager(clusterStateManagerConfiguration, clusterStateStorage, clock);

        var grpcContentTrackerClientConfiguration = new GrpcContentTrackerClient.Configuration(
            TimeSpan.FromSeconds(1),
            RetryPolicyConfiguration.Exponential(
                minimumRetryWindow: TimeSpan.FromMilliseconds(5),
                maximumRetryWindow: TimeSpan.FromMilliseconds(100),
                delta: TimeSpan.FromMilliseconds(10),
                maximumRetryCount: 100));

        var grpcConnectionPoolConfiguration = new ConnectionPoolConfiguration
        {
            ConnectTimeout = configuration.ConnectionTimeout,
            GrpcDotNetOptions = new GrpcDotNetClientOptions()
            {
            },
        };
        var connectionPool = new GrpcConnectionMap(grpcConnectionPoolConfiguration, context, clock);

        // The following is a ugly hack to allow us to communicate with the local content tracker without using gRPC.
        // We will still log everything as if we were using gRPC to communicate, but the calls are entirely in-memory.
        // The problem is that the gRPC service depends on being able to communicate with itself, so we need to somehow
        // break the loop. This is the best way I could come up with.
        var boxedContentTracker = new BoxRef<IGrpcContentTracker>();
        var localClient = new DelayedFixedClientAccessor<IContentTracker>(
            () =>
            {
                var tracker = boxedContentTracker.Value;
                return Task.FromResult((IContentTracker)new GrpcContentTrackerClient(
                    grpcContentTrackerClientConfiguration,
                    new FixedClientAccessor<IGrpcContentTracker>(tracker, configuration.Location)
                ));
            },
            configuration.Location);

        var grpcContentTrackerConnectionPool = new GrpcDotNetClientAccessor<IGrpcContentTracker, IContentTracker>(
            connectionPool,
            (location, service) => new GrpcContentTrackerClient(
                grpcContentTrackerClientConfiguration,
                new FixedClientAccessor<IGrpcContentTracker>(service, location)),
            localClient,
            MetadataServiceSerializer.ClientFactory);

        var masterContentResolver = new MasterContentResolver(
            new MasterContentResolver.Configuration()
            {
                GetLocationsTimeout = configuration.GetLocationsTimeout,
            },
            masterElectionMechanism,
            grpcContentTrackerConnectionPool);

        var masterContentUpdater = new MasterContentUpdater(
            new MasterContentUpdater.Configuration()
            {
                UpdateLocationsTimeout = configuration.UpdateLocationsTimeout,
            },
            masterElectionMechanism,
            grpcContentTrackerConnectionPool);

        var shardManager = new ClusterStateShardManager(clusterStateManager.ClusterState);
        var shardingScheme = new RendezvousConsistentHash<MachineId>(shardManager, id => HashCodeHelper.GetHashCode(id.Index));
        var shardedContentResolver = new ShardedContentResolver(
            new ShardedContentResolver.Configuration()
            {
                GetLocationsTimeout = configuration.GetLocationsTimeout,
            },
            grpcContentTrackerConnectionPool,
            shardingScheme,
            clusterStateManager.ClusterState);

        var shardedContentUpdater = new ShardedContentUpdater(
            new ShardedContentUpdater.Configuration()
            {
                UpdateLocationsTimeout = configuration.UpdateLocationsTimeout,
            },
            grpcContentTrackerConnectionPool,
            shardingScheme,
            clusterStateManager.ClusterState);

        // These get called every time a request comes in via gRPC
        IContentResolver grpcServiceContentResolver;
        IContentUpdater grpcServiceContentUpdater;

        // This gets called every time content is requested by a PlaceFile in the EphemeralContentSession.
        IContentResolver sessionContentResolver;

        // This gets called every time content is added or evicted in the local cache. We'll update the local tracker,
        // and forward the update to the master.
        IContentUpdater changeProcessorUpdater;

        // CODESYNC: ChangeProcessor. The TTL in the ChangeProcessor must match the propagation structure below.
        if (configuration is BuildWideCacheConfiguration)
        {
            // When we're using a build-wide cache, the setup is as follows:
            // 1. The master serves as the source of truth.
            // 2. Workers use the master as their source of truth, propagate all updates to it as well.
            grpcServiceContentResolver = localContentTracker;
            grpcServiceContentUpdater = localContentTracker;

            if (masterElectionMechanism.Role == Role.Master)
            {
                sessionContentResolver = localContentTracker;
                changeProcessorUpdater = localContentTracker;
            }
            else
            {
                sessionContentResolver = new FallbackContentResolver(
                    localContentTracker,
                    masterContentResolver,
                    clock,
                    staleness: configuration.MaximumWorkerStaleness);

                changeProcessorUpdater = new MulticastContentUpdater(
                    new List<MulticastContentUpdater.Destination> {
                        new(localContentTracker, Blocking: true),
                        new(masterContentUpdater, Blocking: false)
                    },
                    inline: configuration.TestInlineChangeProcessing);
            }
        }
        else if (configuration is DatacenterWideCacheConfiguration)
        {
            // When we're using a datacenter-wide cache, the setup is as follows:
            // 1. The master of the current build serves as the source of truth for the current build.
            // 2. Workers use the master as their source of truth, propagate all updates to it as well.
            // 3. The master will forward all updates and queries to the appropriate nodes in the datacenter.
            if (masterElectionMechanism.Role == Role.Master)
            {
                // TODO: send updates back to workers
                // TODO: the first responsible shard should send to the others, maybe?
                var updater = new MulticastContentUpdater(
                    new List<MulticastContentUpdater.Destination> {
                        new(localContentTracker, Blocking: true),
                        new(shardedContentUpdater, Blocking: false)
                    },
                    inline: configuration.TestInlineChangeProcessing);

                var resolver = new FallbackContentResolver(
                    localContentTracker,
                    shardedContentResolver,
                    clock,
                    staleness: configuration.MaximumLeaderStaleness);

                grpcServiceContentResolver = resolver;
                grpcServiceContentUpdater = updater;
                sessionContentResolver = resolver;
                changeProcessorUpdater = updater;
            }
            else
            {
                grpcServiceContentResolver = localContentTracker;
                grpcServiceContentUpdater = localContentTracker;

                sessionContentResolver = new FallbackContentResolver(
                    localContentTracker,
                    masterContentResolver,
                    clock,
                    staleness: configuration.MaximumWorkerStaleness);

                changeProcessorUpdater = new MulticastContentUpdater(
                    new List<MulticastContentUpdater.Destination> {
                        new(localContentTracker, Blocking: true),
                        new(masterContentUpdater, Blocking: false)
                    },
                    inline: configuration.TestInlineChangeProcessing);
            }
        }
        else
        {
            throw new ArgumentException($"Expected {nameof(configuration)} to be a handled subtype of {nameof(Configuration)}. Found {configuration.GetType().FullName} instead.");
        }

        var remoteChangeAnnouncer = new RemoteChangeAnnouncer(
            changeProcessorUpdater,
            clusterStateManager.ClusterState,
            inlineProcessing: configuration.TestInlineChangeProcessing,
            masterElectionMechanism,
            clock);
        persistentCache.Announcer.Set(remoteChangeAnnouncer);

        var service = new GrpcContentTrackerService(grpcServiceContentResolver, grpcServiceContentUpdater);
        boxedContentTracker.Value = service;
        var contentTrackerEndpoint =
            new ProtobufNetGrpcServiceEndpoint<IGrpcContentTracker, GrpcContentTrackerService>(nameof(GrpcContentTrackerService), service);

        var contentStoreConfiguration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(configuration.MaxCacheSizeMb);
        var configurationModel = new ConfigurationModel(
            contentStoreConfiguration,
            ConfigurationSelection.RequireAndUseInProcessConfiguration,
            MissingConfigurationFileOption.DoNotWrite);
        var contentStoreSettings = ContentStoreSettings.DefaultSettings;
        var contentStore = new FileSystemContentStore(
            PassThroughFileSystem.Default,
            clock,
            configuration.RootPath / "store",
            configurationModel,
            distributedStore: null,
            settings: contentStoreSettings);

        var changeProcessor = new LocalChangeProcessor(
            clusterStateManager.ClusterState,
            localContentTracker,
            changeProcessorUpdater,
            masterElectionMechanism,
            inlineProcessing: configuration.TestInlineChangeProcessing,
            clock);
        contentStore.Store.Announcer = changeProcessor;

        var storesByName = new Dictionary<string, IContentStore>() { { "Default", contentStore }, };
        var copyServerConfiguration = new GrpcCopyServer.Configuration();
        var copyServer = new GrpcCopyServer(context.TracingContext.Logger, storesByName, copyServerConfiguration);
        var distributedContentCopierConfiguration =
            new DistributedContentCopier.Configuration()
            {
                RetryIntervalForCopies = new[]
                                         {
                                             TimeSpan.FromMilliseconds(1),
                                             TimeSpan.FromMilliseconds(2),
                                             TimeSpan.FromMilliseconds(4),
                                             TimeSpan.FromMilliseconds(16),
                                         },
                BandwidthConfigurations = new BandwidthConfiguration[]
                                          {
                                              new()
                                              {
                                                  ConnectionTimeout = configuration.ConnectionTimeout,
                                                  EnableNetworkCopySpeedCalculation = true,
                                                  FailFastIfServerIsBusy = true,
                                                  Interval = TimeSpan.FromMilliseconds(50),
                                                  InvalidateOnTimeoutError = false,
                                                  RequiredBytes = "10 MB".ToSize(),
                                              },
                                              new()
                                              {
                                                  ConnectionTimeout = configuration.ConnectionTimeout,
                                                  EnableNetworkCopySpeedCalculation = true,
                                                  FailFastIfServerIsBusy = true,
                                                  Interval = TimeSpan.FromMilliseconds(50),
                                                  InvalidateOnTimeoutError = false,
                                                  RequiredBytes = "1 MB".ToSize(),
                                              },
                                          },
                MaxRetryCount = 4,
            };
        // TODO: investigate why PutFile high percentiles are slow
        // TODO: broadcast async request batching
        var remoteFileCopier = new GrpcFileCopier(context, new GrpcFileCopierConfiguration()
        {
            UseUniversalLocations = true,
            // TODO: see
            // TODO: fix timeouts here!!
            GrpcCopyClientCacheConfiguration = new GrpcCopyClientCacheConfiguration()
            {
                GrpcCopyClientConfiguration = new GrpcCopyClientConfiguration()
                {
                    ConnectionTimeout = configuration.ConnectionTimeout,
                    DisconnectionTimeout = configuration.ConnectionTimeout,
                    TimeToFirstByteTimeout = configuration.ConnectionTimeout,
                    GrpcCoreClientOptions = new GrpcCoreClientOptions(),
                    GrpcDotNetClientOptions = new GrpcDotNetClientOptions(),
                    BandwidthCheckerConfiguration = new BandwidthChecker.Configuration(
                        // We use a very high frequency bandwidth checker because we need to very quickly cancel copies
                        // that are too slow.
                        bandwidthCheckInterval: TimeSpan.FromMilliseconds(1),
                        minimumBandwidthMbPerSec: 10.0,
                        maxBandwidthLimit: double.MaxValue,
                        bandwidthLimitMultiplier: 1.0,
                        historicalBandwidthRecordsStored: 64),
                }
            }
        });
        var contentCopier = new DistributedContentCopier(
            distributedContentCopierConfiguration,
            PassThroughFileSystem.Default,
            remoteFileCopier,
            // This is actually useless. We don't support proactive copies (on purpose)
            copyRequester: remoteFileCopier,
            clock,
            context.TracingContext.Logger);

        var localHostInfo = configuration.Location.ToGrpcHost();
        int? grpcPort = null;
        int? encryptedGrpcPort = null;
        if (localHostInfo.Encrypted)
        {
            encryptedGrpcPort = localHostInfo.Port;
        }
        else
        {
            grpcPort = localHostInfo.Port;
        }

        var host = new EphemeralHost(
            new EphemeralCacheConfiguration
            {
                // We use gRPC.Core for the server because we have observed issues with gRPC.NET in practice.
                GrpcConfiguration = new GrpcCoreServerHostConfiguration(GrpcPort: grpcPort, EncryptedGrpcPort: encryptedGrpcPort, GrpcCoreServerOptions: new GrpcCoreServerOptions()
                {

                    Http2MinTimeBetweenPingsMs = (int)Math.Ceiling(TimeSpan.FromSeconds(1).TotalMilliseconds),
                    Http2MaxPingsWithoutData = 120,
                    Http2MaxPingStrikes = 2,
                    KeepaliveTimeMs = (int)Math.Ceiling(TimeSpan.FromMinutes(1).TotalMilliseconds),
                    KeepaliveTimeoutMs = (int)Math.Ceiling(TimeSpan.FromSeconds(30).TotalMilliseconds),
                    MaxConcurrentStreams = int.MaxValue,
                    MaxConnectionIdleMs = (int)Math.Ceiling(TimeSpan.FromMinutes(1).TotalMilliseconds),
                }),
                Workspace = configuration.RootPath / "workspace",
            },
            clock,
            PassThroughFileSystem.Default,
            localContentTracker,
            changeProcessor,
            clusterStateManager,
            contentTrackerEndpoint,
            copyServer,
            contentCopier,
            grpcClusterStateEndpoint,
            masterElectionMechanism,
            sessionContentResolver,
            remoteChangeAnnouncer);

        var ephemeralContentStore = new EphemeralContentStore(
            contentStore,
            persistentCache.Cache,
            host);

        var passthroughCache = new PassthroughCache(ephemeralContentStore, persistentCache.Cache);

        return Task.FromResult(new CreateResult(host, passthroughCache));
    }

    /// <summary>
    /// A cache session that passes through all calls to the underlying sessions specified in the constructor.
    /// </summary>
    private class PassthroughCacheSession
        : StartupShutdownComponentBase, ICacheSessionWithLevelSelectors
    {
        protected override Tracer Tracer { get; } = new(nameof(PassthroughCacheSession));

        public string Name { get; }

        private readonly IContentSession _contentSession;
        private readonly ICacheSessionWithLevelSelectors _cacheSession;

        public PassthroughCacheSession(string name, IContentSession contentSession, ICacheSessionWithLevelSelectors cacheSession)
        {
            Name = name;
            _contentSession = contentSession;
            _cacheSession = cacheSession;

            LinkLifetime(_contentSession);
            LinkLifetime(cacheSession);
        }

        /// <inheritdoc />
        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentSession.PinAsync(context, contentHash, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration config)
        {
            return _contentSession.PinAsync(context, contentHashes, config);
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentSession.OpenStreamAsync(context, contentHash, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentSession.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentSession.PinAsync(context, contentHashes, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentSession.PlaceFileAsync(context, hashesWithPaths, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentSession.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentSession.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentSession.PutStreamAsync(context, hashType, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentSession.PutStreamAsync(context, contentHash, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _cacheSession.GetSelectors(context, weakFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _cacheSession.GetContentHashListAsync(context, strongFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _cacheSession.AddOrGetContentHashListAsync(context, strongFingerprint, contentHashListWithDeterminism, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(
            Context context,
            IEnumerable<Task<StrongFingerprint>> strongFingerprints,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _cacheSession.IncorporateStrongFingerprintsAsync(context, strongFingerprints, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            return _cacheSession.GetLevelSelectorsAsync(context, weakFingerprint, cts, level);
        }
    }

    private class PassthroughCache : StartupShutdownComponentBase, IFullCache
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(PassthroughCache));

        public Guid Id { get; } = Guid.NewGuid();

        private readonly IContentStore _ephemeralCache;
        private readonly ICache _persistentCache;

        public PassthroughCache(IContentStore ephemeralCache, IFullCache persistentCache)
        {
            _ephemeralCache = ephemeralCache;
            _persistentCache = persistentCache;

            LinkLifetime(ephemeralCache);
            LinkLifetime(persistentCache);
        }

        public CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreatePassthroughSession(context, name, implicitPin);
        }

        CreateSessionResult<IContentSession> IContentStore.CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            var session = CreatePassthroughSession(context, name, implicitPin);
            return new CreateSessionResult<IContentSession>(session.Session!);
        }

        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name, IContentSession contentSession, bool automaticallyOverwriteContentHashLists)
        {
            var session = CreatePassthroughSession(context, name, ImplicitPin.None);
            return new CreateSessionResult<IMemoizationSession>(session.Session!);
        }

        private CreateSessionResult<ICacheSession> CreatePassthroughSession(Context context, string name, ImplicitPin implicitPin)
        {
            // ImplicitPin is explicitly set to None because we absolutely never EVER want to pin here.
            var contentSession = _ephemeralCache.CreateSession(context, name, ImplicitPin.None).ThrowIfFailure();
            CreateSessionResult<ICacheSession> cacheSession = _persistentCache.CreateSession(context, name, ImplicitPin.None).ThrowIfFailure();
            var session = cacheSession.Session! as ICacheSessionWithLevelSelectors;
            Contract.Assert(session != null, "An invalid session was returned");
            return new CreateSessionResult<ICacheSession>(new PassthroughCacheSession(name, contentSession.Session!, session!));
        }

        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
        {
            return _ephemeralCache.DeleteAsync(context, contentHash, deleteOptions);
        }

        public void PostInitializationCompleted(Context context)
        {
            _ephemeralCache.PostInitializationCompleted(context);
        }

        public async Task<GetStatsResult> GetStatsAsync(Context context)
        {
            var output = new CounterSet();
            var ephemeralStats = await _ephemeralCache.GetStatsAsync(context).ThrowIfFailureAsync();
            output.Merge(ephemeralStats.CounterSet, "Ephemeral.");

            var persistentStats = await _persistentCache.GetStatsAsync(context).ThrowIfFailureAsync();
            output.Merge(persistentStats.CounterSet, "Persistent.");

            return new GetStatsResult(output);
        }

        public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            return _persistentCache.EnumerateStrongFingerprints(context);
        }
    }
}
