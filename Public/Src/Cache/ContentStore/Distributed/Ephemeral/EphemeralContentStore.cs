// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

public class EphemeralCacheConfiguration
{
    public required GrpcCoreServerHostConfiguration GrpcConfiguration { get; init; }

    public required AbsolutePath Workspace { get; init; }

    public TimeSpan PutElisionRaceTimeout { get; init; } = TimeSpan.FromMinutes(1);

    public int PutElisionMinimumReplication { get; init; } = 1;

    public TimeSpan PutElisionMaximumStaleness { get; init; } = TimeSpan.FromDays(1);
}

public class EphemeralHost : StartupShutdownComponentBase
{
    protected override Tracer Tracer { get; } = new(nameof(EphemeralHost));

    public EphemeralCacheConfiguration Configuration { get; }

    public IAbsFileSystem FileSystem { get; }

    public IClock Clock { get; }

    public ILocalContentTracker LocalContentTracker { get; }

    public LocalChangeProcessor LocalChangeProcessor { get; }

    public ClusterStateManager ClusterStateManager { get; }

    public IGrpcServiceEndpoint GrpcContentTrackerEndpoint { get; }

    public IGrpcServiceEndpoint? GrpcClusterStateEndpoint { get; set; }

    public IMasterElectionMechanism MasterElectionMechanism { get; }

    public GrpcCopyServer CopyServer { get; }

    public DistributedContentCopier ContentCopier { get; }

    public IContentResolver ContentResolver { get; }

    public RemoteChangeAnnouncer RemoteChangeAnnouncer { get; }

    private readonly GrpcCoreServerHost _initializer = new();

    internal readonly LockSet<ContentHash> RemoteFetchLocks = new();

    public EphemeralHost(
        EphemeralCacheConfiguration configuration,
        IClock clock,
        IAbsFileSystem fileSystem,
        ILocalContentTracker localContentTracker,
        LocalChangeProcessor localChangeProcessor,
        ClusterStateManager clusterStateManager,
        IGrpcServiceEndpoint grpcContentTrackerEndpoint,
        GrpcCopyServer copyServer,
        DistributedContentCopier contentCopier,
        IGrpcServiceEndpoint? grpcClusterStateEndpoint,
        IMasterElectionMechanism masterElectionMechanism,
        IContentResolver contentResolver,
        RemoteChangeAnnouncer remoteChangeAnnouncer)
    {
        Configuration = configuration;
        FileSystem = fileSystem;
        LocalContentTracker = localContentTracker;
        LocalChangeProcessor = localChangeProcessor;
        ClusterStateManager = clusterStateManager;
        GrpcContentTrackerEndpoint = grpcContentTrackerEndpoint;
        CopyServer = copyServer;
        ContentCopier = contentCopier;
        GrpcClusterStateEndpoint = grpcClusterStateEndpoint;
        MasterElectionMechanism = masterElectionMechanism;
        ContentResolver = contentResolver;
        RemoteChangeAnnouncer = remoteChangeAnnouncer;
        Clock = clock;

        LinkLifetime(MasterElectionMechanism);

        if (grpcClusterStateEndpoint is IStartupShutdownSlim grpcClusterStateEndpointStartupShutdown)
        {
            LinkLifetime(grpcClusterStateEndpointStartupShutdown);
        }
        LinkLifetime(ClusterStateManager);

        LinkLifetime(LocalContentTracker);
        LinkLifetime(LocalChangeProcessor);

        if (GrpcContentTrackerEndpoint is IStartupShutdownSlim grpcContentTrackerEndpointStartupShutdown)
        {
            LinkLifetime(grpcContentTrackerEndpointStartupShutdown);
        }

        LinkLifetime(CopyServer);
        LinkLifetime(ContentCopier);

        LinkLifetime(RemoteChangeAnnouncer);
    }

    protected override async Task<BoolResult> StartupComponentAsync(OperationContext context)
    {
        // We need a way to identify leaders of builds that are running at any given point. We do this by piggybacking
        // on to the machine's state:
        // - Build orchestrators will have Role = Master and State = Open.
        // - Build workers will have Role = Worker and State = Closed.
        // - Machines from builds that have completed or exited will have State = DeadUnavailable.
        if (MasterElectionMechanism.Role != Role.Master)
        {
            await ClusterStateManager.HeartbeatAsync(context, MachineState.Closed).ThrowIfFailureAsync();
        }

        var endpoints = new List<IGrpcServiceEndpoint>() { GrpcContentTrackerEndpoint, CopyServer.GrpcAdapter };
        if (GrpcClusterStateEndpoint is not null)
        {
            endpoints.Add(GrpcClusterStateEndpoint);
        }

        await _initializer.StartAsync(
            context,
            Configuration.GrpcConfiguration,
            endpoints).ThrowIfFailureAsync();
        return BoolResult.Success;
    }

    protected override async Task<BoolResult> ShutdownComponentAsync(OperationContext context)
    {
        var result = BoolResult.Success;

        // If this fails to heartbeat, there's absolutely nothing we can do for error recovery other than accept that
        // we're screwed, so don't throw!
        result &= await ClusterStateManager.HeartbeatAsync(context, MachineState.DeadUnavailable);

        result &= await _initializer.StopAsync(context, Configuration.GrpcConfiguration);

        return result;
    }
};

public class EphemeralContentStore : StartupShutdownComponentBase, IContentStore
{
    protected override Tracer Tracer { get; } = new(nameof(EphemeralContentStore));

    private readonly IContentStore _local;
    private readonly IContentStore _persistent;
    private readonly EphemeralHost _ephemeralHost;

    public EphemeralContentStore(IContentStore local, IContentStore persistent, EphemeralHost ephemeralHost)
    {
        _local = local;
        _persistent = persistent;
        _ephemeralHost = ephemeralHost;

        LinkLifetime(local);
        LinkLifetime(persistent);
        LinkLifetime(ephemeralHost);
    }

    protected override Task<BoolResult> StartupComponentAsync(OperationContext context)
    {
        _ephemeralHost.FileSystem.CreateDirectory(_ephemeralHost.Configuration.Workspace);
        return BoolResult.SuccessTask;
    }

    protected override Task<BoolResult> ShutdownComponentAsync(OperationContext context)
    {
        _ephemeralHost.FileSystem.DeleteDirectory(_ephemeralHost.Configuration.Workspace, DeleteOptions.All);
        return BoolResult.SuccessTask;
    }

    public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
    {
        // We never enable implicit pinning for the local cache, because the local cache is expected to be ephemeral
        // and very small.
        var localResult = _local.CreateSession(context, $"EphemeralCache({name}/Local)", ImplicitPin.None).ThrowIfFailure();
        var remoteResult = _persistent.CreateSession(context, $"EphemeralCache({name}/Persistent)", implicitPin).ThrowIfFailure();
        return new CreateSessionResult<IContentSession>(new EphemeralContentSession($"EphemeralCache({name}/Datacenter)", localResult.Session!, remoteResult.Session!, _ephemeralHost));
    }

    public async Task<GetStatsResult> GetStatsAsync(Context context)
    {
        var counters = new CounterSet();
        var local = await _local.GetStatsAsync(context);
        var persistent = await _persistent.GetStatsAsync(context);
        if (local.Succeeded)
        {
            counters.Merge(local.CounterSet, $"{nameof(local)}.");
        }

        if (persistent.Succeeded)
        {
            counters.Merge(persistent.CounterSet, $"{nameof(persistent)}.");
        }

        return new GetStatsResult(counters);
    }

    public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
    {
        // Deleting from here only deletes from the local store. Cluster-wide deletion not yet supported.
        return _local.DeleteAsync(context, contentHash, deleteOptions);
    }

    public void PostInitializationCompleted(Context context)
    {
    }
}
