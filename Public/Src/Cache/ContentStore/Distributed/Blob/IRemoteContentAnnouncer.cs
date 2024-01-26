// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

public abstract record RemoteContentEvent(AbsoluteBlobPath Path, ContentHash Hash);

public sealed record AddEvent(AbsoluteBlobPath Path, ContentHash Hash, long Size) : RemoteContentEvent(Path, Hash);

public sealed record DeleteEvent(AbsoluteBlobPath Path, ContentHash Hash) : RemoteContentEvent(Path, Hash);

public sealed record TouchEvent(AbsoluteBlobPath Path, ContentHash Hash, long Size) : RemoteContentEvent(Path, Hash);

public interface IRemoteContentAnnouncer
{
    Task Notify(OperationContext context, RemoteContentEvent @event);
}
