// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// This class is the glue between <see cref="AzureBlobStorageContentSession"/> and <see cref="EphemeralContentStore"/>.
/// 
/// It allows the former to notify the latter about any changes that are detected in Azure Storage. The key operating
/// word here is _detected_. We don't know when the change actually happens, only when we try to do something and see
/// it.
/// </summary>
public class RemoteChangeAnnouncer : StartupShutdownComponentBase, IRemoteContentAnnouncer
{
    protected override Tracer Tracer { get; } = new(nameof(RemoteChangeAnnouncer));

    private readonly IClock _clock;
    private readonly IContentUpdater _updater;
    private readonly ClusterState _clusterState;
    private readonly IMasterElectionMechanism _masterElectionMechanism;
    private readonly bool _inlineProcessing;

    public RemoteChangeAnnouncer(
        IContentUpdater updater,
        ClusterState clusterState,
        bool inlineProcessing,
        IMasterElectionMechanism masterElectionMechanism,
        IClock? clock = null)
    {
        _updater = updater;
        _clock = clock ?? SystemClock.Instance;
        _clusterState = clusterState;
        _inlineProcessing = inlineProcessing;
        _masterElectionMechanism = masterElectionMechanism;
        LinkLifetime(_masterElectionMechanism);
        LinkLifetime(_updater);
    }

    public async Task Notify(OperationContext context, RemoteContentEvent @event)
    {
        var task = ProcessRemoteChangeAsync(context, @event);

        if (_inlineProcessing)
        {
            await task;
        }
        else
        {
            task.FireAndForget(context, traceFailures: false);
        }
    }

    private async Task ProcessRemoteChangeAsync(OperationContext context, RemoteContentEvent @event)
    {
        if (!StartupCompleted)
        {
            // This can happen because the lifetime of the RemoteChangeProcessor is complicated.
            Tracer.Warning(context, $"Dropping event because {nameof(RemoteChangeAnnouncer)} hasn't been started up yet. Event=[{@event}]");
            return;
        }

        _ = await context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                var operation = ChangeStampOperation.Add;
                long size = -1;
                switch (@event)
                {
                    case AddEvent ev:
                        size = ev.Size;
                        break;
                    case TouchEvent ev:
                        size = ev.Size;
                        break;
                    case DeleteEvent ev:
                        operation = ChangeStampOperation.Delete;
                        break;
                    default:
                        return new BoolResult(errorMessage: $"Unknown event type: {@event.GetType()}");
                }

                var location = @event.Path.ContainerPath.ToMachineLocation();
                if (!_clusterState.TryResolveMachineId(location, out var machineId))
                {
                    return new BoolResult(errorMessage: $"Could not find a machine ID for {@event.Path.ContainerPath}");
                }

                var stamp = ChangeStamp.Create(sequenceNumber: new SequenceNumber(0), timestampUtc: _clock.UtcNow, operation);
                var stamped = new Stamped<MachineId>(stamp, machineId);

                var contentEntry = new ContentEntry()
                {
                    Hash = @event.Hash,
                    Size = size,
                    Operations = new List<Stamped<MachineId>>(capacity: 1) { stamped, },
                };

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

                await _updater.UpdateLocationsAsync(context, UpdateLocationsRequest.SingleHash(contentEntry, timeToLive)).ThrowIfFailureAsync();

                return BoolResult.Success;
            },
            extraEndMessage: _ => $"Event=[{@event}]",
            traceErrorsOnly: true,
            traceOperationStarted: false);
    }
}
