// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
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
        public TimeSpan UpdateLocationsTimeout { get; set; }

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
            traceErrorsOnly: true,
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

    private readonly IReadOnlyList<Destination> _destinations;

    private readonly int _blocking;

    public readonly record struct Destination(
        IContentUpdater Updater,
        bool Blocking);

    public MulticastContentUpdater(IReadOnlyList<Destination> destinations, bool inline)
    {
        if (inline)
        {
            _destinations = destinations.Select(cast => cast with { Blocking = true }).ToList();
        }
        else
        {
            _destinations = destinations;
        }

        var blocking = 0;
        foreach (var destination in _destinations)
        {
            LinkLifetime(destination.Updater);

            if (destination.Blocking)
            {
                blocking++;
            }
        }

        _blocking = blocking;
    }

    public async Task<BoolResult> UpdateLocationsAsync(OperationContext context, UpdateLocationsRequest request)
    {
        if (request.Drop)
        {
            return BoolResult.Success;
        }

        request = request.Hop();

        var tasks = new List<Task<BoolResult>>(capacity: _blocking);
        foreach (var updater in _destinations)
        {
            var pending = updater.Updater.UpdateLocationsAsync(context, request);
            if (updater.Blocking)
            {
                tasks.Add(pending);
            }
            else
            {
                _ = pending.FireAndForgetErrorsAsync(context);
            }
        }

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
        public required int WriteCandidates { get; init; }

        public required TimeSpan BatchingUpdateLocationsTimeout { get; init; }

        public required int BatchingMaxDegreeOfParallelism { get; init; }

        public required int BatchingMaxBatchSize { get; init; }

        public required TimeSpan BatchingInterval { get; init; }
    }

    protected override Tracer Tracer { get; } = new(nameof(ShardedContentUpdater));

    private readonly IClientAccessor<MachineLocation, IContentUpdater> _clients;
    private readonly IMultiCandidateShardingScheme<int, MachineId> _scheme;
    private readonly ClusterState _clusterState;

    private readonly BatchingQueue<MachineLocation, UpdateLocationsRequest, BoolResult> _batchingQueue;

    new internal Configuration Settings;

    public ShardedContentUpdater(
        Configuration settings,
        IClientAccessor<MachineLocation, IContentUpdater> clients,
        IMultiCandidateShardingScheme<int, MachineId> scheme,
        ClusterState clusterState)
    : base(settings)
    {
        Settings = settings;
        _clients = clients;
        _scheme = scheme;
        _clusterState = clusterState;

        _batchingQueue = new BatchingQueue<MachineLocation, UpdateLocationsRequest, BoolResult>(ProcessBatch, settings.BatchingInterval, settings.BatchingMaxBatchSize, settings.BatchingMaxDegreeOfParallelism);
        LinkLifetime(_clients);
    }

    protected override Task<BoolResult> ShutdownComponentAsync(OperationContext context)
    {
        _batchingQueue.Dispose();
        return base.ShutdownComponentAsync(context);
    }

    private async Task ProcessBatch(MachineLocation location, IReadOnlyList<BatchingQueue<MachineLocation, UpdateLocationsRequest, BoolResult>.Item> pending, CancellationToken cancellationToken)
    {
        using var context = TrackShutdown(StartupContext, cancellationToken);

        var request = UpdateLocationsRequest.Merge(pending.Select(item => item.Value));

        try
        {
            var task = _clients.WithClientAsync(
                context,
                request,
                location,
                (context, client, request) => client.UpdateLocationsAsync(context, request),
                Counters[Counter.UpdateLocationsSuccess],
                Counters[Counter.UpdateLocationsFailure],
                Counters[Counter.UpdateLocationsTotal]);

            var result = await TaskUtilities.WithTimeoutAsync(task, Settings.BatchingUpdateLocationsTimeout, context.Context.Token);

            foreach (var item in pending)
            {
                item.Succeed(result);
            }
        }
        catch (Exception ex)
        {
            var result = new BoolResult(ex, $"Batch request against {location} failed");
            foreach (var item in pending)
            {
                item.Fail(ex);
            }
        }
    }

    /// <inheritdoc />
    public override async Task<BoolResult> ForwardUpdatedLocationsAsync(OperationContext context, UpdateLocationsRequest request)
    {
        var candidates = ((Configuration)Settings).WriteCandidates;

        var tasks = new List<Task<BoolResult>>(capacity: candidates);
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

                tasks.Add(_batchingQueue.Enqueue(location, singleton, context.Token));
            }
        }

        var results = await TaskUtilities.SafeWhenAll(tasks);
        return results.And();
    }
}
