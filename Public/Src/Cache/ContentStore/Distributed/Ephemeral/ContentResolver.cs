// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
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
/// A <see cref="IContentResolver"/> that reaches out to the master machine to resolve locations. The master is
/// determined via the <see cref="IMasterElectionMechanism"/> passed into it.
/// </summary>
public class MasterContentResolver : StartupShutdownComponentBase, IContentResolver
{
    public enum Counter
    {
        [CounterType(CounterType.Stopwatch)]
        GetLocationsCalls,
        GetLocationTotal,
        GetLocationSuccess,
        GetLocationFailure,
    }

    public record Configuration
    {
        public TimeSpan GetLocationsTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
    }

    protected override Tracer Tracer { get; } = new(nameof(MasterContentResolver));

    public CounterCollection<Counter> Counters { get; } = new();

    private readonly Configuration _configuration;
    private readonly IMasterElectionMechanism _masterElectionMechanism;

    private readonly IClientAccessor<MachineLocation, IContentResolver> _clients;

    public MasterContentResolver(Configuration configuration, IMasterElectionMechanism masterElectionMechanism, IClientAccessor<MachineLocation, IContentResolver> clients)
    {
        _configuration = configuration;
        _masterElectionMechanism = masterElectionMechanism;
        _clients = clients;
        LinkLifetime(_masterElectionMechanism);
        LinkLifetime(_clients);
    }

    /// <inheritdoc />
    public Task<Result<GetLocationsResponse>> GetLocationsAsync(OperationContext context, GetLocationsRequest request)
    {
        Contract.Assert(_masterElectionMechanism.Role != Role.Master, $"Attempt to use {nameof(MasterContentResolver)} from the master machine in {_masterElectionMechanism.Master}");

        return context.PerformOperationWithTimeoutAsync(
            Tracer,
             async context =>
            {
                return await _clients.WithClientAsync(
                    context,
                    request,
                    _masterElectionMechanism.Master,
                    (context, client, request) => client.GetLocationsAsync(context, request),
                    Counters[Counter.GetLocationSuccess],
                    Counters[Counter.GetLocationFailure],
                    Counters[Counter.GetLocationTotal]);
            },
            extraEndMessage: result =>
                             {
                                 var baseline = $"Master=[{_masterElectionMechanism.Master}] Request=[{request}]";
                                 if (result.Succeeded)
                                 {
                                     return $"{baseline} Response=[{result.Value}]";
                                 }

                                 return baseline;
                             },
            timeout: _configuration.GetLocationsTimeout,
            traceOperationStarted: false,
            counter: Counters[Counter.GetLocationsCalls]);
    }
}

/// <summary>
/// A <see cref="IContentResolver"/> that reaches out to a set of nodes to resolve locations. The set of nodes is
/// determined via a <see cref="IMultiCandidateShardingScheme{TKey,TLoc}"/> passed into it.
/// </summary>
public class ShardedContentResolver : StartupShutdownComponentBase, IContentResolver
{
    public enum Counter
    {
        [CounterType(CounterType.Stopwatch)]
        GetLocationsCalls,
        GetLocationTotal,
        GetLocationSuccess,
        GetLocationFailure,
    }

    public record Configuration
    {
        /// <summary>
        /// The number of hosts to reach out to when querying locations for a given hash.
        /// </summary>
        /// <remarks>
        /// This number presents a trade-off between accuracy and performance. The higher the number, the more accurate
        /// our answer for locations will be (and therefore the most likely we will be to obtain a file from the
        /// cluster), and the more expensive the query will be (because we'll need to reach out to all involved hosts).
        /// </remarks>
        public int ReadCandidates { get; set; } = 2;

        /// <summary>
        /// How long to wait for any given <see cref="GetLocationsAsync"/> request to complete.
        /// </summary>
        /// <remarks>
        /// This number must be kept low because the Ephemeral L3 is essentially a balancing act between the
        /// performance of asking Azure Storage for a file, and the performance of fetching it from the cluster. If we
        /// take too long to answer to any given request, it will likely be a lot faster to fetch from Azure Storage.
        /// </remarks>
        public TimeSpan GetLocationsTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
    }

    protected override Tracer Tracer { get; } = new(nameof(ShardedContentResolver));

    public CounterCollection<Counter> Counters { get; } = new();

    private readonly Configuration _configuration;
    private readonly IClientAccessor<MachineLocation, IContentResolver> _clients;

    private readonly IMultiCandidateShardingScheme<int, MachineId> _scheme;
    private readonly ClusterState _clusterState;

    public ShardedContentResolver(Configuration configuration, IClientAccessor<MachineLocation, IContentResolver> clients, IMultiCandidateShardingScheme<int, MachineId> scheme, ClusterState clusterState)
    {
        _configuration = configuration;
        _clients = clients;
        _scheme = scheme;
        _clusterState = clusterState;

        LinkLifetime(_clients);
    }

    /// <inheritdoc />
    public Task<Result<GetLocationsResponse>> GetLocationsAsync(OperationContext context, GetLocationsRequest request)
    {
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
             async context =>
            {
                var candidates = _configuration.ReadCandidates;
                var pending = new List<Task<Result<GetLocationsResponse>>>(capacity: request.Hashes.Count * candidates);

                // Query the responsible nodes in the cluster
                foreach (var hash in request.Hashes)
                {
                    // It is very important to mark this request as non-recursive. What this will cause is that other
                    // nodes won't run this logic again, and therefore avoid an infinite loop of RPC requests.
                    var singleton = GetLocationsRequest.SingleHash(hash, recursive: false);
                    var shards = _scheme.Locate(BlobCacheShardingKey.FromShortHash(hash).Key, candidates);

                    foreach (var shard in shards)
                    {
                        if (shard.Location == _clusterState.PrimaryMachineId)
                        {
                            // We don't self-ask for locations. This is expected to be handled elsewhere.
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
                            (context, client, request) => client.GetLocationsAsync(context, request),
                            Counters[Counter.GetLocationSuccess],
                            Counters[Counter.GetLocationFailure],
                            Counters[Counter.GetLocationTotal]));
                    }
                }

                if (pending.Count == 0)
                {
                    return Result.Success(GetLocationsResponse.Empty(request));
                }

                var results = await TaskUtilities.SafeWhenAll(pending);
                var responses = results.SelectMany(
                    result =>
                    {
                        if (result.Succeeded)
                        {
                            return result.Value.Results;
                        }

                        return Array.Empty<ContentEntry>();
                    });

                return Result.Success(GetLocationsResponse.Gather(request, responses));
            },
            extraEndMessage: result =>
                             {
                                 var baseline = $"Request=[{request}]";
                                 if (result.Succeeded)
                                 {
                                     return $"{baseline} Response=[{result.Value}]";
                                 }

                                 return baseline;
                             },
            timeout: _configuration.GetLocationsTimeout,
            traceOperationStarted: false,
            counter: Counters[Counter.GetLocationsCalls]);
    }
}

public class FallbackContentResolver : StartupShutdownComponentBase, IContentResolver
{
    protected override Tracer Tracer { get; } = new(nameof(FallbackContentResolver));

    private readonly IContentTracker _local;
    private readonly IContentResolver _fallbackResolver;

    public FallbackContentResolver(IContentTracker local, IContentResolver fallbackResolver)
    {
        _local = local;
        _fallbackResolver = fallbackResolver;
        Tracer = new Tracer($"{nameof(FallbackContentResolver)}({local.GetType().Name} -> {fallbackResolver.GetType().Name})");

        LinkLifetime(_local);
        LinkLifetime(_fallbackResolver);
    }

    public async Task<Result<GetLocationsResponse>> GetLocationsAsync(OperationContext context, GetLocationsRequest request)
    {
        if (request.Recursive)
        {
            // TODO: elision of remote queries based on timestamp?
            var fallbackTask = _fallbackResolver.GetLocationsAsync(context, request);
            var localTask = _local.GetLocationsAsync(context, request);

            await TaskUtilities.SafeWhenAll(localTask, fallbackTask);
            var local = await localTask;
            var fallback = await fallbackTask;

            // TODO: avoid allocations here, and nicer error behavior
            var responses = new[] { local.GetValueOr(GetLocationsResponse.Empty(request)), fallback.GetValueOr(GetLocationsResponse.Empty(request)) };
            var response = GetLocationsResponse.Gather(request, responses.SelectMany(response => response.Results));

            // TODO: we don't need an update with everything we already know, just the diff
            var update = UpdateLocationsRequest.FromGetLocationsResponse(response);
            await _local.UpdateLocationsAsync(context, update).IgnoreFailure();

            return Result.Success(response);
        }
        else
        {
            return await _local.GetLocationsAsync(context, request);
        }
    }
}
