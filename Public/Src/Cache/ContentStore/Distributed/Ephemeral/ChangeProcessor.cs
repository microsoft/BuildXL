// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// This class processes inbound notifications from <see cref="FileSystemContentStore"/> into updates for
/// <see cref="IContentUpdater"/>. It's the glue logic between FSCS and the Ephemeral cache.
/// </summary>
public class ChangeProcessor : StartupShutdownComponentBase, IContentChangeAnnouncer
{
    public enum Counter
    {
        [CounterType(CounterType.Stopwatch)]
        ProcessLocalChangeCalls,
        ProcessLocalAdd,
        ProcessLocalDelete,
    }

    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new(nameof(ChangeProcessor));

    public CounterCollection<Counter> Counters { get; } = new();

    private readonly ClusterState _clusterState;
    private readonly ILocalContentTracker _localContentTracker;
    private readonly IMasterElectionMechanism _masterElectionMechanism;
    private readonly IClock _clock;
    private readonly IContentUpdater _updater;

    /// <summary>
    /// Whether to inline the processing of updates from <see cref="FileSystemContentStore"/>. This causes the FSCS
    /// operations to wait for the update to be processed, which may involve gRPC traffic.
    /// </summary>
    /// <remarks>
    /// Intended to be used only for testing. It's used to guarantee strong consistency in tests. This is needed in
    /// tests because we want to assert conditions to be true at specific points in time, but isn't needed in
    /// production because we don't care about individual operations failing.
    /// </remarks>
    private readonly bool _inlineProcessing;

    public ChangeProcessor(
        ClusterState clusterState,
        ILocalContentTracker localContentTracker,
        IContentUpdater updater,
        IMasterElectionMechanism masterElectionMechanism,
        bool inlineProcessing,
        IClock? clock = null)
    {
        _clusterState = clusterState;
        _localContentTracker = localContentTracker;
        _updater = updater;
        _masterElectionMechanism = masterElectionMechanism;
        _inlineProcessing = inlineProcessing;
        _clock = clock ?? SystemClock.Instance;

        LinkLifetime(_localContentTracker);
        LinkLifetime(_updater);
    }

    public async Task ProcessLocalChangeAsync(Context tracingContext, ChangeStampOperation operation, ContentHashWithSize contentHashWithSize)
    {
        using var cancellableContext = TrackShutdown(tracingContext);
        var context = cancellableContext.Context;
        await context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                ChangeStamp changeStamp = GetNextChangeStamp(operation, contentHashWithSize.Hash);
                var stamped = new Stamped<MachineId>(changeStamp, _clusterState.PrimaryMachineId);

                // Default here is exactly the number of hops a worker's update has to traverse before it reaches the
                // DHT in a Datacenter-wide setting
                // WARNING: we really, really want the smallest TTL we can possibly have, because the TTL manages how
                // much background load we'll have.
                var timeToLive = 5;
                if (_masterElectionMechanism.Role == Role.Master)
                {
                    // The master is the only one that can update the DHT, so it can use a smaller TTL.
                    timeToLive = 2;
                }

                var request = UpdateLocationsRequest.SingleHash(
                                      new ContentEntry
                                      {
                                          Hash = contentHashWithSize.Hash,
                                          Size = contentHashWithSize.Size,
                                          Operations = new List<Stamped<MachineId>> { stamped },
                                      }, timeToLive);

                return await _updater.UpdateLocationsAsync(context, request);
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

    #region IContentChangeAnnouncer implementation

    /// <inheritdoc />
    public async Task ContentAdded(Context context, ContentHashWithSize contentHashWithSize)
    {
        // Here and in the method below we don't care about the result of the gossip operation. The reason is that any
        // failures will be logged there and we don't want to log them here as well. Moreover, we don't want to block
        // the caller because of the gossip operation, as it can be very slow and we don't want to hang
        // FileSystemContentStore.
        var task = ProcessLocalChangeAsync(context, ChangeStampOperation.Add, contentHashWithSize);
        if (_inlineProcessing)
        {
            await task;
        }
        else
        {
            task.FireAndForget(context, traceFailures: false);
        }
    }

    /// <inheritdoc />
    public async Task ContentEvicted(Context context, ContentHashWithSize contentHashWithSize)
    {
        // TODO: change IContentChangeAnnouncer so it can announce multiple items at once (this is actually what makes sense)
        var task = ProcessLocalChangeAsync(context, ChangeStampOperation.Delete, contentHashWithSize);
        if (_inlineProcessing)
        {
            await task;
        }
        else
        {
            task.FireAndForget(context, traceFailures: false);
        }
    }

    #endregion
}

