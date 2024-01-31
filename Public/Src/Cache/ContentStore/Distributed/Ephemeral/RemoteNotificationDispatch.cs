// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// Allows plugging in a <see cref="IRemoteContentAnnouncer"/> at runtime after objects have been constructed. This is
/// needed because <see cref="AzureBlobStorageContentSession"/> is constructed before
/// <see cref="RemoteChangeAnnouncer"/> is, since the cache itself needs to be constructed for
/// <see cref="EphemeralContentStore"/> to be initialized.
/// </summary>
public class RemoteNotificationDispatch : IRemoteContentAnnouncer
{
    protected Tracer Tracer { get; } = new(nameof(RemoteNotificationDispatch));

    private readonly BoxRef<IRemoteContentAnnouncer?> _inner = new();

    internal void Set(IRemoteContentAnnouncer announcer)
    {
        _inner.Value = announcer;
    }

    public async Task Notify(OperationContext context, RemoteContentEvent @event)
    {
        if (_inner.Value is not null)
        {
            await _inner.Value.Notify(context, @event);
        }
    }
}
