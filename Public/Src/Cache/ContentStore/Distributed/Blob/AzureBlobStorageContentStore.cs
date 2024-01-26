// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Configuration for <see cref="AzureBlobStorageContentStore"/>.
/// </summary>
public sealed record AzureBlobStorageContentStoreConfiguration
{
    public required IBlobCacheTopology Topology { get; init; }

    public IRemoteContentAnnouncer? Announcer { get; init; } = null;

    public TimeSpanSetting StorageInteractionTimeout { get; init; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// A <see cref="IContentStore"/> implementation backed by azure storage.
/// </summary>
public class AzureBlobStorageContentStore : StartupShutdownComponentBase, IContentStore
{
    /// <inheritdoc />
    protected sealed override Tracer Tracer { get; } = new(nameof(AzureBlobStorageContentStore));

    private readonly AzureBlobStorageContentStoreConfiguration _configuration;

    /// <nodoc />
    public AzureBlobStorageContentStore(AzureBlobStorageContentStoreConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
    {
        using var guard = TrackShutdown(context, default);
        var operationContext = guard.Context;

        return operationContext.PerformOperation(
            Tracer,
            () => new CreateSessionResult<IContentSession>(CreateSessionCore(name, implicitPin)),
            traceOperationStarted: false,
            messageFactory: _ => $"Name=[{name}] ImplicitPin=[{implicitPin}]");
    }

    private IContentSession CreateSessionCore(string name, ImplicitPin implicitPin)
    {
        return new AzureBlobStorageContentSession(
            new AzureBlobStorageContentSession.Configuration(
                Name: name,
                ImplicitPin: implicitPin,
                StorageInteractionTimeout: _configuration.StorageInteractionTimeout,
                Announcer: _configuration.Announcer),
            store: this);
    }

    /// <inheritdoc />
    public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
    {
        return Task.FromResult(new DeleteResult(DeleteResult.ResultCode.ContentNotDeleted, contentHash, -1));
    }

    /// <inheritdoc />
    public Task<GetStatsResult> GetStatsAsync(Context context)
    {
        // TODO: Provide some reasonable set of counters.
        // For now, we just return an empty set
        // In some cases returning an error here will also make metadata stats to not be reported, so we avoid that.
        return Task.FromResult(new GetStatsResult(new CounterSet()));
    }

    /// <inheritdoc />
    public void PostInitializationCompleted(Context context)
    {
        // Unused on purpose
    }

    internal Task<(BlobClient Client, AbsoluteBlobPath Path)> GetBlobClientAsync(OperationContext context, ContentHash contentHash)
    {
        return _configuration.Topology.GetBlobClientAsync(context, contentHash);
    }
}
