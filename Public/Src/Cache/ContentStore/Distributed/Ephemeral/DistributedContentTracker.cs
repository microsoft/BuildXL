// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using ResultsExtensions = BuildXL.Cache.ContentStore.Interfaces.Results.ResultsExtensions;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// Takes care of gossiping changes about content amongst all <see cref="IContentTracker"/> in the cluster.
/// </summary>
/// <remarks>
/// This class implements <see cref="IContentTracker"/> not out of necessity but to ease testing.
/// </remarks>
public class DistributedContentTracker : StartupShutdownComponentBase, IDistributedContentTracker
{
    public enum Counter
    {
        [CounterType(CounterType.Stopwatch)]
        ProcessLocalChangeCalls,

        ProcessLocalAdd,
        ProcessLocalDelete,

        [CounterType(CounterType.Stopwatch)]
        UpdateLocationsCalls,

        [CounterType(CounterType.Stopwatch)]
        GetLocationsCalls,
        GetLocationTotal,
        GetLocationSuccess,
        GetLocationFailure,

        [CounterType(CounterType.Stopwatch)]
        BroadcastCalls,
        BroadcastTotal,
        BroadcastSuccess,
        BroadcastFailure,
    }

    public record Configuration
    {
        public int GossipCandidates { get; init; } = 5;

        public int ReadCandidates { get; init; } = 5;

        public TimeSpan BroadcastTimeout { get; set; } = TimeSpan.MaxValue;

        public TimeSpan? BroadcastTracingInterval { get; set; } = null;

        public int BroadcastConcurrency { get; set; } = 1024;

        public TimeSpan GetLocationsTimeout { get; set; } = TimeSpan.MaxValue;

        public TimeSpan? GetLocationsTracingInterval { get; set; }

        public int GetLocationsConcurrency { get; set; } = 1024;
    }

    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new(nameof(DistributedContentTracker));

    public CounterCollection<Counter> Counters { get; } = new();

    private readonly ILocalContentTracker _localContentTracker;

    /// <summary>
    /// <see cref="_clients"/> is an abstraction layer over how to obtain <see cref="IContentTracker"/> instances. In
    /// practice, this is used in 2 ways:
    ///  1. In production, where it produces instances that talk to other machines in the cluster via RPC.
    ///  2. In tests, where is produces instances that directly call onto the <see cref="_localContentTracker"/> of the
    ///     respective <see cref="DistributedContentTracker"/> for the machine.
    /// </summary>
    /// <remarks>
    /// It is assumed that the <see cref="IContentTracker"/> instances produced by <see cref="_clients"/> manage
    /// retries and timeouts for the RPCs they perform if they do so. This class doesn't do any of that.
    /// </remarks>
    private readonly IClientAccessor<MachineLocation, IContentTracker> _clients;

    private readonly IClock _clock;
    private readonly IMasterElectionMechanism _masterElectionMechanism;

    private readonly IMultiCandidateShardingScheme<int, MachineId> _scheme;
    private readonly ClusterState _clusterState;

    /// <summary>
    /// Limits the number of concurrent <see cref="BroadcastAsync"/> calls.
    /// </summary>
    private readonly SemaphoreSlim _concurrentBroadcastGate;

    /// <summary>
    /// Limits the number of concurrent <see cref="GetLocationsAsync"/> calls.
    /// </summary>
    private readonly SemaphoreSlim _concurrentGetLocationsGate;

    private readonly Configuration _configuration;

    public DistributedContentTracker(
        Configuration configuration,
        ClusterState clusterState,
        IMultiCandidateShardingScheme<int, MachineId> scheme,
        ILocalContentTracker localContentTracker,
        IClientAccessor<MachineLocation, IContentTracker> clients,
        IMasterElectionMechanism masterElectionMechanism,
        IClock? clock = null)
    {
        _configuration = configuration;
        _clusterState = clusterState;
        _scheme = scheme;
        _localContentTracker = localContentTracker;
        _clients = clients;
        _masterElectionMechanism = masterElectionMechanism;
        _clock = clock ?? SystemClock.Instance;

        _concurrentBroadcastGate = new SemaphoreSlim(_configuration.BroadcastConcurrency);
        _concurrentGetLocationsGate = new SemaphoreSlim(_configuration.GetLocationsConcurrency);

        LinkLifetime(_localContentTracker);
        LinkLifetime(_clients);
        LinkLifetime(_masterElectionMechanism);
    }

    /// <summary>
    /// This function is the entry point for the <see cref="FileSystemNotificationReceiver"/>. It is called to notify
    /// the tracker about a change in the local content store, so we can propagate it to the rest of the cluster.
    /// </summary>
    public async Task ProcessLocalChangeAsync(Context tracingContext, ChangeStampOperation operation, ContentHashWithSize contentHashWithSize)
    {
        using var cancellableContext = TrackShutdown(tracingContext);
        var context = cancellableContext.Context;
        await context.PerformOperationAsync(
            Tracer,
            () =>
            {
                ChangeStamp changeStamp = GetNextChangeStamp(operation, contentHashWithSize.Hash);
                var stamped = new Stamped<MachineId>(changeStamp, _clusterState.PrimaryMachineId);

                var request = new UpdateLocationsRequest
                {
                    Entries = new List<ContentEntry>()
                                            {
                                                new()
                                                {
                                                    Hash = contentHashWithSize.Hash,
                                                    Size = contentHashWithSize.Size,
                                                    Operations = new List<Stamped<MachineId>> { stamped },
                                                },
                                            },
                };

                return UpdateLocationsAsync(context, request);
            },
            traceOperationStarted: false,
            traceOperationFinished: false,
            counter: Counters[Counter.ProcessLocalChangeCalls]).IgnoreFailure();

        switch (operation)
        {
            case ChangeStampOperation.Add:
                Counters[Counter.ProcessLocalAdd].Increment();
                break;
            case ChangeStampOperation.Delete:
                Counters[Counter.ProcessLocalDelete].Increment();
                break;
        }
    }

    private ChangeStamp GetNextChangeStamp(ChangeStampOperation operation, ShortHash hash)
    {
        var lastSequenceNumber = _localContentTracker.GetSequenceNumber(hash, _clusterState.PrimaryMachineId);
        return ChangeStamp.Create(lastSequenceNumber.Next(), _clock.UtcNow, operation);
    }

    /// <inheritdoc />
    public Task<BoolResult> UpdateLocationsAsync(OperationContext context, UpdateLocationsRequest request)
    {
        return context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                // Update the local content tracker so we know that we have the content.
                await _localContentTracker.UpdateLocationsAsync(context, request).IgnoreFailure();

                // Broadcast to the rest of the cluster.
                return await BroadcastAsync(context, request);
            },
            traceOperationStarted: false,
            traceOperationFinished: false,
            counter: Counters[Counter.UpdateLocationsCalls]);
    }

    private async Task<BoolResult> BroadcastAsync(OperationContext context, UpdateLocationsRequest request)
    {
        // TODO: this code doesn't deal with failures. Failures at this point are considered permanent, but we may
        // consider extending the scope of broadcast to deal with failures.
        using var guard = await _concurrentBroadcastGate.AcquireAsync(context.Token);

        return await context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var pending = new List<Task>();

                // Send the update to the build leader, as long as it's not the current machine.
                if (_masterElectionMechanism.Role != Role.Master)
                {
                    pending.Add(
                        WithClientAsync(
                            context,
                            request,
                            _masterElectionMechanism.Master,
                            (context, tracker, request) => tracker.UpdateLocationsAsync(context, request),
                            Counter.BroadcastSuccess,
                            Counter.BroadcastFailure,
                            Counter.BroadcastTotal).ThrowIfFailureAsync());
                }

                // Send the update to the corresponding locator nodes in the cluster.
                foreach (var entry in request.Entries)
                {
                    var shards = _scheme.Locate(BlobCacheShardingKey.FromShortHash(entry.Hash).Key, _configuration.GossipCandidates);
                    foreach (var shard in shards)
                    {
                        // Don't send an update to ourselves, we already did it
                        if (shard.Location == _clusterState.PrimaryMachineId)
                        {
                            continue;
                        }

                        if (!_clusterState.TryResolve(shard.Location, out var location))
                        {
                            Tracer.Error(context, $"Sharding scheme determined {shard.Location} is responsible for {entry.Hash}, but failed to resolve machine ID");
                            continue;
                        }

                        if (!_clusterState.OpenMachines.Contains(shard.Location))
                        {
                            Tracer.Warning(context, $"Sharding scheme determined {shard.Location} is responsible for {entry.Hash}, but it is currently not in Open state");
                            continue;
                        }

                        // Don't send an update to the build leader, we already did it
                        if (location == _masterElectionMechanism.Master)
                        {
                            continue;
                        }

                        pending.Add(
                            WithClientAsync(
                                context,
                                request,
                                location,
                                (context, tracker, request) => tracker.UpdateLocationsAsync(context, request),
                                Counter.BroadcastSuccess,
                                Counter.BroadcastFailure,
                                Counter.BroadcastTotal).IgnoreFailure());
                    }
                }

                await TaskUtilities.SafeWhenAll(pending);

                return BoolResult.Success;
            },
            timeout: _configuration.BroadcastTimeout,
            traceOperationStarted: false,
            // TODO: trace information about the request
            pendingOperationTracingInterval: _configuration.BroadcastTracingInterval,
            counter: Counters[Counter.BroadcastCalls]);
    }

    private async Task<TResult> WithClientAsync<TRequest, TResult>(
        OperationContext context,
        TRequest request,
        MachineLocation location,
        Func<OperationContext, IContentTracker, TRequest, Task<TResult>> func,
        Counter success,
        Counter failure,
        Counter tally)
        where TResult : ResultBase
    {
        try
        {
            var result = await _clients.UseAsync(
                context,
                location,
                contentTracker => func(context, contentTracker, request));

            if (result.Succeeded)
            {
                Counters[success].Increment();
            }
            else
            {
                Counters[failure].Increment();
            }

            return result;
        }
        catch (Exception exception)
        {
            Counters[failure].Increment();
            return (new ErrorResult(exception)).AsResult<TResult>();
        }
        finally
        {
            Counters[tally].Increment();
        }
    }

    /// <inheritdoc />
    public async Task<Result<GetLocationsResponse>> GetLocationsAsync(OperationContext context, GetLocationsRequest request)
    {
        using var guard = await _concurrentGetLocationsGate.AcquireAsync(context.Token);

        return await context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var responses = new List<ContentEntry>();

                // Query the master
                if (_masterElectionMechanism.Role != Role.Master)
                {
                    var masterResult = await WithClientAsync(
                               context,
                               request,
                               _masterElectionMechanism.Master,
                               (context, client, request) => client.GetLocationsAsync(context, request),
                               Counter.GetLocationSuccess,
                               Counter.GetLocationFailure,
                               Counter.GetLocationTotal);
                    if (masterResult.Succeeded)
                    {
                        responses.AddRange(masterResult.Value.Results);
                    }
                }

                // Query the responsible nodes in the cluster
                foreach (var hash in request.Hashes)
                {
                    var shards = _scheme.Locate(BlobCacheShardingKey.FromShortHash(hash).Key, _configuration.ReadCandidates);

                    foreach (var shard in shards)
                    {
                        if (shard.Location == _clusterState.PrimaryMachineId)
                        {
                            // We are responsible for this hash, but we have already queried the local content tracker.
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

                        var result = await WithClientAsync(
                            context,
                            request,
                            location,
                            (context, client, request) => client.GetLocationsAsync(context, request),
                            Counter.GetLocationSuccess,
                            Counter.GetLocationFailure,
                            Counter.GetLocationTotal);
                        if (!result.Succeeded)
                        {
                            Tracer.Warning(context, $"Failed to get locations for {hash} from {location}: {result}");
                        }
                        else if (result.Value.Results.Count == 0)
                        {
                            Tracer.Warning(context, $"No locations for {hash} from {location}");
                        }
                        else
                        {
                            responses.AddRange(result.Value.Results);
                        }
                    }
                }

                // Insert into our local database and report back
                await _localContentTracker.UpdateLocationsAsync(context, new UpdateLocationsRequest() { Entries = responses }).IgnoreFailure();
                return await _localContentTracker.GetLocationsAsync(context, request);

                // TODO: incorporate data received into local tracker.

                // TODO: lazy merge responses.

                // TODO: failure handling. When finding that locations are unavailable, we might want to extend the query scope.
                // TODO: this code should allow for lazily populating the returned response. The reason for that is that it may take several milliseconds or seconds to obtain an answer from any given node.
            },
            timeout: _configuration.GetLocationsTimeout,
            traceOperationStarted: false,
            // TODO: trace information about the request
            pendingOperationTracingInterval: _configuration.GetLocationsTracingInterval,
            counter: Counters[Counter.GetLocationsCalls]);
    }
}
