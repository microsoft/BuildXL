// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// This <see cref="IContentUpdater"/> implementation forwards requests to the next hop in the cluster. It is used as a
/// base class for updaters that do any kind of forwarding.
/// </summary>
public abstract class ForwardingContentUpdaterBase : StartupShutdownComponentBase, IContentUpdater
{
    public enum Counter
    {
        [CounterType(CounterType.Stopwatch)]
        UpdateLocationsCalls,
        UpdateLocationsTotal,
        UpdateLocationsSuccess,
        UpdateLocationsFailure,
    }

    public record Configuration
    {
        public TimeSpan UpdateLocationsTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

        public TimeSpan? UpdateLocationsTracingInterval { get; set; }
    }

    public CounterCollection<Counter> Counters { get; } = new();

    protected readonly Configuration Settings;

    protected ForwardingContentUpdaterBase(Configuration settings)
    {
        Settings = settings;
    }

    public async Task<BoolResult> UpdateLocationsAsync(OperationContext context, UpdateLocationsRequest request)
    {
        if (request.Drop)
        {
            // The request's TTL is expired, so we can skip the update.
            return BoolResult.Success;
        }

        var next = request.Hop();

        using var shutdownContext = TrackShutdown(context);
        return await shutdownContext.Context.PerformOperationWithTimeoutAsync(
            Tracer,
            context => ForwardUpdatedLocationsAsync(context, next),
            timeout: Settings.UpdateLocationsTimeout,
            traceOperationStarted: false,
            // TODO: trace information about who the request was forwarded to
            pendingOperationTracingInterval: Settings.UpdateLocationsTracingInterval,
            extraEndMessage: _ => $"Request=[{request}]",
            counter: Counters[Counter.UpdateLocationsCalls]
        );
    }

    public abstract Task<BoolResult> ForwardUpdatedLocationsAsync(OperationContext context, UpdateLocationsRequest request);
}

public class MulticastContentUpdater : StartupShutdownComponentBase, IContentUpdater
{
    protected override Tracer Tracer { get; } = new(nameof(MulticastContentUpdater));

    private readonly IContentUpdater[] _updaters;

    public MulticastContentUpdater(IContentUpdater[] updaters)
    {
        _updaters = updaters;
        foreach (var updater in _updaters)
        {
            LinkLifetime(updater);
        }
    }

    public async Task<BoolResult> UpdateLocationsAsync(OperationContext context, UpdateLocationsRequest request)
    {
        if (request.Drop)
        {
            return BoolResult.Success;
        }

        request = request.Hop();
        var tasks = _updaters.Select(updater => updater.UpdateLocationsAsync(context, request));
        var responses = await TaskUtilities.SafeWhenAll(tasks);
        return responses.And();
    }
}

public class MasterContentUpdater : ForwardingContentUpdaterBase
{
    protected override Tracer Tracer { get; } = new(nameof(MasterContentUpdater));

    private readonly IMasterElectionMechanism _masterElectionMechanism;
    private readonly IClientAccessor<MachineLocation, IContentUpdater> _clients;

    public MasterContentUpdater(
        Configuration settings,
        IMasterElectionMechanism masterElectionMechanism,
        IClientAccessor<MachineLocation, IContentUpdater> clients)
    : base(settings)
    {
        _masterElectionMechanism = masterElectionMechanism;
        _clients = clients;
        LinkLifetime(_masterElectionMechanism);
        LinkLifetime(_clients);
    }

    /// <inheritdoc />
    public override Task<BoolResult> ForwardUpdatedLocationsAsync(OperationContext context, UpdateLocationsRequest request)
    {
        return _clients.WithClientAsync(
            context,
            request,
            _masterElectionMechanism.Master,
            (context, client, request) => client.UpdateLocationsAsync(context, request),
            Counters[Counter.UpdateLocationsSuccess],
            Counters[Counter.UpdateLocationsFailure],
            Counters[Counter.UpdateLocationsTotal]);
    }
}

public class ShardedContentUpdater : ForwardingContentUpdaterBase
{
    public new record Configuration : ForwardingContentUpdaterBase.Configuration
    {
        public int WriteCandidates { get; set; } = 2;
    }

    protected override Tracer Tracer { get; } = new(nameof(ShardedContentUpdater));

    private readonly IClientAccessor<MachineLocation, IContentUpdater> _clients;
    private readonly IMultiCandidateShardingScheme<int, MachineId> _scheme;
    private readonly ClusterState _clusterState;

    public ShardedContentUpdater(
        Configuration settings,
        IClientAccessor<MachineLocation, IContentUpdater> clients,
        IMultiCandidateShardingScheme<int, MachineId> scheme,
        ClusterState clusterState)
    : base(settings)
    {
        _clients = clients;
        _scheme = scheme;
        _clusterState = clusterState;

        LinkLifetime(_clients);
    }

    /// <inheritdoc />
    public override async Task<BoolResult> ForwardUpdatedLocationsAsync(OperationContext context, UpdateLocationsRequest request)
    {
        var pending = new List<Task<BoolResult>>();
        var candidates = ((Configuration)Settings).WriteCandidates;

        foreach (var entry in request.Entries)
        {
            var hash = entry.Hash;
            var shards = _scheme.Locate(BlobCacheShardingKey.FromShortHash(hash).Key, candidates);

            var singleton = request.Derived(entry);
            foreach (var shard in shards)
            {
                if (shard.Location == _clusterState.PrimaryMachineId)
                {
                    continue;
                }

                if (!_clusterState.TryResolve(shard.Location, out var location))
                {
                    Tracer.Error(context, $"Sharding scheme determined {shard.Location} is responsible for {hash}, but failed to resolve machine ID");
                    continue;
                }

                if (!_clusterState.OpenMachines.Contains(shard.Location))
                {
                    Tracer.Warning(context, $"Sharding scheme determined {shard.Location} is responsible for {hash}, but it is currently not in Open state");
                    continue;
                }

                pending.Add(_clients.WithClientAsync(
                    context,
                    singleton,
                    location,
                    (context, client, request) => client.UpdateLocationsAsync(context, request),
                    Counters[Counter.UpdateLocationsSuccess],
                    Counters[Counter.UpdateLocationsFailure],
                    Counters[Counter.UpdateLocationsTotal]));
            }
        }

        var results = await TaskUtilities.SafeWhenAll(pending);
        return results.And();
    }
}
