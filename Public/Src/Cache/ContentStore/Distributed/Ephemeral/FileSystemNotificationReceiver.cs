// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// This glue class receives notifications from <see cref="FileSystemContentStore"/> and hands them off to
/// <see cref="DistributedContentTracker"/>.
/// </summary>
public class FileSystemNotificationReceiver : IContentChangeAnnouncer
{
    private readonly DistributedContentTracker _gossip;

    public FileSystemNotificationReceiver(DistributedContentTracker gossip)
    {
        _gossip = gossip;
    }

    public Task ContentAdded(Context context, ContentHashWithSize contentHashWithSize)
    {
        // Here and in the method below we don't care about the result of the gossip operation. The reason is that any
        // failures will be logged there and we don't want to log them here as well. Moreover, we don't want to block
        // the caller because of the gossip operation, as it can be very slow and we don't want to hang
        // FileSystemContentStore.
        _gossip.ProcessLocalChangeAsync(context, ChangeStampOperation.Add, contentHashWithSize).FireAndForget(context, traceFailures: false);
        return Task.CompletedTask;
    }

    // TODO: change IContentChangeAnnouncer so it can announce multiple items at once (this is actually what makes sense)
    public Task ContentEvicted(Context context, ContentHashWithSize contentHashWithSize)
    {
        _gossip.ProcessLocalChangeAsync(context, ChangeStampOperation.Delete, contentHashWithSize).FireAndForget(context, traceFailures: false);
        return Task.CompletedTask;
    }
}
