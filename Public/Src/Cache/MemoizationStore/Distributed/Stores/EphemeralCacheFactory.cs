// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
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
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
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
        /// Credentials for the storage account we use to store metadata about the cluster.
        /// </summary>
        public required IAzureStorageCredentials StorageCredentials { get; init; }

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
    };

    /// <summary>
    /// Creates a P2P cache.
    /// </summary>
    public static async Task<IFullCache> CreateAsync(OperationContext context, Configuration configuration, IFullCache persistentCache)
    {
        return (await CreateInternalAsync(context, configuration, persistentCache)).Cache;
    }

    internal static Task<CreateResult> CreateInternalAsync(OperationContext context, Configuration configuration, IFullCache persistentCache)
    {
        GrpcEnvironment.Initialize(context.TracingContext.Logger, new GrpcEnvironmentOptions()
        {
            LoggingVerbosity = GrpcEnvironmentOptions.GrpcVerbosity.Disabled,
        });

        return configuration switch
        {
            DatacenterWideCacheConfiguration datacenterWideCacheConfiguration => CreateDatacenterWideCacheAsync(context, datacenterWideCacheConfiguration, persistentCache),
            BuildWideCacheConfiguration buildWideCacheConfiguration => CreateBuildWideCacheAsync(context, buildWideCacheConfiguration, persistentCache),
            _ => throw new NotSupportedException($"Cache type {configuration.GetType().Name} is not supported.")
        };
    }

    /// <summary>
    /// This version can be used to split up the cache into different logical caches after release. This is useful to
    /// allow backwards-incompatible changes. Essentially, all nodes in a different <see cref="DatacenterCacheVersion"/>
    /// will only see nodes with the same <see cref="DatacenterCacheVersion"/> as reachable.
    ///
    /// Deployment of features that cause backwards-incompatible changes should be done as follows:
    ///  1. Perform feature changes.
    ///  2. Modify <see cref="DatacenterCacheVersion"/> to a never-seen-before value (ex: increment it!)
    ///  3. Deploy new version as usual.
    /// </summary>
    private static readonly string DatacenterCacheVersion = "20230919";

    private static Task<CreateResult> CreateDatacenterWideCacheAsync(
        OperationContext context,
        DatacenterWideCacheConfiguration configuration,
        IFullCache persistentCache,
        IClock? clock = null)
    {
        clock ??= SystemClock.Instance;

        if (string.IsNullOrEmpty(configuration.Universe))
        {
            configuration = configuration with { Universe = DatacenterWideCacheConfiguration.DefaultUniverse };
        }

        var blobClusterStateStorageConfiguration = new BlobClusterStateStorageConfiguration()
        {
            Storage = new BlobClusterStateStorageConfiguration.StorageSettings(configuration.StorageCredentials, ContainerName: "ephemeral", FolderName: "clusterState"),
            BlobFolderStorageConfiguration = new()
            {
                StorageInteractionTimeout = configuration.StorageInteractionTimeout,
            },
            FileName = $"clusterState-{DatacenterCacheVersion}-{configuration.Universe}.json",
            RecomputeConfiguration = new ClusterStateRecomputeConfiguration(),
        };
        var clusterStateStorage = new BlobClusterStateStorage(blobClusterStateStorageConfiguration, clock);

        var masterElectionMechanism = CreateMasterElectionMechanism(configuration.Location, configuration.Leader);
        return CreateInternalAsync(
            context,
            configuration,
            masterElectionMechanism,
            clusterStateStorage,
            persistentCache,
            grpcClusterStateEndpoint: null,
            clock);
    }

    private static Task<CreateResult> CreateBuildWideCacheAsync(
        OperationContext context,
        BuildWideCacheConfiguration configuration,
        IFullCache persistentCache,
        IClock? clock = null)
    {
        clock ??= SystemClock.Instance;

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
                    TimeSpan.FromMinutes(1),
                    RetryPolicyConfiguration.Exponential()),
                accessor: new DelayedFixedClientAccessor<IGrpcClusterStateStorage>(
                    async () =>
                    {
                        var connectionHandle = new ConnectionHandle(
                            context,
                            configuration.Leader,
                            // Please note, the following parameter is useless because we should be setting up ports on
                            // all machine locations, so it should never be used.
                            GrpcConstants.DefaultEphemeralLeaderGrpcPort,
                            // Allow waiting for the leader to setup for up to 30m
                            connectionTimeout: configuration.ConnectionTimeout);
                        await connectionHandle.StartupAsync(context).ThrowIfFailureAsync();

                        return connectionHandle.Channel.CreateGrpcService<IGrpcClusterStateStorage>(MetadataServiceSerializer.ClientFactory);
                    },
                    configuration.Leader
                ));
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

    internal record CreateResult(EphemeralHost Host, IFullCache Cache);

    /// <nodoc />
    private static Task<CreateResult> CreateInternalAsync(
        OperationContext context,
        Configuration configuration,
        IMasterElectionMechanism masterElectionMechanism,
        IClusterStateStorage clusterStateStorage,
        IFullCache persistentCache,
        IGrpcServiceEndpoint? grpcClusterStateEndpoint,
        IClock clock)
    {
#if NETCOREAPP3_1_OR_GREATER
        var useGrpcDotNet = true;
#else
        var useGrpcDotNet = false;
#endif

        var (address, port) = configuration.Location.ExtractHostInfo();
        Contract.Requires(port is not null, $"Port missing from the configured reachable DNS name: {configuration.Location}");

        var localContentTracker = new LocalContentTracker();

        var clusterStateManagerConfiguration = new ClusterStateManager.Configuration
        {
            PrimaryLocation = configuration.Location,
            UpdateInterval = configuration.HeartbeatInterval,
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
            // Please note, the following parameter is useless because we should be setting up ports on all machine
            // locations, so it should never be used.
            DefaultPort = GrpcConstants.DefaultEphemeralGrpcPort,
            UseGrpcDotNet = useGrpcDotNet,
            GrpcDotNetOptions = new GrpcDotNetClientOptions()
            {
                // We explicitly disable gRPC client-side tracing because it's too noisy.
                MinLogLevelVerbosity = null,
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
            localClient);

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

        var changeProcessor = new ChangeProcessor(
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
                                                  InvalidateOnTimeoutError = true,
                                                  RequiredBytes = "10 MB".ToSize(),
                                              },
                                              new()
                                              {
                                                  ConnectionTimeout = configuration.ConnectionTimeout,
                                                  EnableNetworkCopySpeedCalculation = true,
                                                  FailFastIfServerIsBusy = true,
                                                  Interval = TimeSpan.FromMilliseconds(50),
                                                  InvalidateOnTimeoutError = true,
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
                    UseGrpcDotNetVersion = useGrpcDotNet,
                    ConnectionTimeout = configuration.ConnectionTimeout,
                    DisconnectionTimeout = configuration.ConnectionTimeout,
                    ConnectOnStartup = true,
                    PropagateCallingMachineName = true,
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

        var host = new EphemeralHost(
            new EphemeralCacheConfiguration
            {
                // We use gRPC.Core for the server because we have observed issues with gRPC.NET in practice.
                GrpcConfiguration = new GrpcCoreServerHostConfiguration(GrpcPort: port!.Value, GrpcCoreServerOptions: new GrpcCoreServerOptions()
                {
                    // We have connection pools that we self-manage, so no need for the server to do it.
                    MaxConcurrentStreams = int.MaxValue,
                    MaxConnectionIdleMs = int.MaxValue,
                    MaxConnectionAgeMs = int.MaxValue,
                    KeepaliveTimeMs = (int)Math.Floor(TimeSpan.FromMinutes(5).TotalMilliseconds),
                    KeepaliveTimeoutMs = (int)Math.Floor(TimeSpan.FromSeconds(10).TotalMilliseconds),
                    KeepalivePermitWithoutCalls = 1,
                    Http2MinPingIntervalWithoutDataMs = 0,
                    Http2MaxPingStrikes = 0,
                    Http2MaxPingsWithoutData = 0,
                    ServerHandshakeTimeoutMs = (int)Math.Floor(TimeSpan.FromSeconds(10).TotalMilliseconds),
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
            sessionContentResolver);

        var ephemeralContentStore = new EphemeralContentStore(
            contentStore,
            persistentCache,
            host);

        //var cache = new OneLevelCache(
        //    () => ephemeralContentStore,
        //    () => (IMemoizationStore)persistentCache,
        //    new OneLevelCacheBaseConfiguration(Guid.NewGuid(), PassContentToMemoization: false));

        var cc = new PassthroughCache(ephemeralContentStore, persistentCache);

        return Task.FromResult(new CreateResult(Host: host, Cache: cc));
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
            var contentSession = _ephemeralCache.CreateSession(context, name, implicitPin).ThrowIfFailure();
            CreateSessionResult<ICacheSession> cacheSession = _persistentCache.CreateSession(context, name, implicitPin).ThrowIfFailure();
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
