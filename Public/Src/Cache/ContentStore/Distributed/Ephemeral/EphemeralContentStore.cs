// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using Microsoft.Azure.Amqp.Serialization;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

public class EphemeralCacheConfiguration
{
    public required GrpcCoreServerHostConfiguration GrpcConfiguration { get; init; }

    public required AbsolutePath Workspace { get; init; }
}

public class EphemeralHost : StartupShutdownComponentBase
{
    protected override Tracer Tracer { get; } = new(nameof(EphemeralHost));

    public EphemeralCacheConfiguration Configuration { get; }

    public IAbsFileSystem FileSystem { get; }

    public ILocalContentTracker LocalContentTracker { get; }

    public IDistributedContentTracker DistributedContentTracker { get; }

    public ClusterStateManager ClusterStateManager { get; }

    public IGrpcServiceEndpoint GrpcContentTrackerEndpoint { get; }

    public IGrpcServiceEndpoint? GrpcClusterStateEndpoint { get; set; }

    public GrpcCopyServer CopyServer { get; }

    public DistributedContentCopier ContentCopier { get; }

    private readonly GrpcCoreServerHost _initializer = new();

    public EphemeralHost(
        EphemeralCacheConfiguration configuration,
        IAbsFileSystem fileSystem,
        ILocalContentTracker localContentTracker,
        IDistributedContentTracker distributedContentTracker,
        ClusterStateManager clusterStateManager,
        IGrpcServiceEndpoint grpcContentTrackerEndpoint,
        GrpcCopyServer copyServer,
        DistributedContentCopier contentCopier,
        IGrpcServiceEndpoint? grpcClusterStateEndpoint)
    {
        Configuration = configuration;
        FileSystem = fileSystem;
        LocalContentTracker = localContentTracker;
        DistributedContentTracker = distributedContentTracker;
        ClusterStateManager = clusterStateManager;
        GrpcContentTrackerEndpoint = grpcContentTrackerEndpoint;
        CopyServer = copyServer;
        ContentCopier = contentCopier;
        GrpcClusterStateEndpoint = grpcClusterStateEndpoint;

        LinkLifetime(ClusterStateManager);
        if (grpcClusterStateEndpoint is IStartupShutdownSlim grpcClusterStateEndpointStartupShutdown)
        {
            LinkLifetime(grpcClusterStateEndpointStartupShutdown);
        }

        LinkLifetime(LocalContentTracker);
        LinkLifetime(DistributedContentTracker);

        if (GrpcContentTrackerEndpoint is IStartupShutdownSlim grpcContentTrackerEndpointStartupShutdown)
        {
            LinkLifetime(grpcContentTrackerEndpointStartupShutdown);
        }

        LinkLifetime(CopyServer);
        LinkLifetime(ContentCopier);
    }

    protected override async Task<BoolResult> StartupComponentAsync(OperationContext context)
    {
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
        await _initializer.StopAsync(context, Configuration.GrpcConfiguration).ThrowIfFailureAsync();
        return BoolResult.Success;
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
