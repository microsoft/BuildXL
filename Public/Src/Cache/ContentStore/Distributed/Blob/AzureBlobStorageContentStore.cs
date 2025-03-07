// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
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

public readonly record struct UploadOperationTimeout(long MaximumSizeBytes, TimeSpan Timeout);

/// <summary>
/// Configuration for <see cref="AzureBlobStorageContentStore"/>.
/// </summary>
public sealed record AzureBlobStorageContentStoreConfiguration
{
    public required IBlobCacheTopology Topology { get; init; }

    public IRemoteContentAnnouncer? Announcer { get; init; } = null;

    public TimeSpanSetting StorageInteractionTimeout { get; init; } = TimeSpan.FromMinutes(30);

    public int ParallelHashingFileSizeBoundary { get; init; } = (int)"4 MB".ToSize();

    public int InitialTransferSize { get; init; } = (int)"4 MB".ToSize();

    public int MaximumTransferSize { get; init; } = (int)"200 MB".ToSize();

    public int MaximumConcurrency { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Timeout for upload operations based on the size of the upload.
    /// </summary>
    /// <remarks>
    /// - This is an absolute timeout for the upload operation, meant to ensure we don't get stuck on a single upload,
    ///   they should in practice finish significanly quicker than this.
    /// - This list must be sorted by ascending size as it's looked up assuming it.
    /// </remarks>
    public List<UploadOperationTimeout> UploadSafeguardTimeouts { get; init; } = new List<UploadOperationTimeout>()
    {
        new UploadOperationTimeout("4 MB".ToSize(), TimeSpan.FromMinutes(30)),
        new UploadOperationTimeout("1 GB".ToSize(), TimeSpan.FromHours(1)),
        new UploadOperationTimeout("8 GB".ToSize(), TimeSpan.FromHours(2)),

        // This is a catch-all for all cases that go over the sizes above.
        new UploadOperationTimeout(long.MaxValue, TimeSpan.FromHours(24)),
    };

    /// <summary>
    /// Whether this session is supposed to be read-only or read-write.
    /// </summary>
    public bool IsReadOnly { get; internal set; }

    /// <summary>
    /// The amount of time since the last touch before we allow issuing a write-touch which will cause the content's
    /// ETag to change.
    /// </summary>
    /// <remarks>
    /// This number was picked to ensure we don't do any hard touches on content that was recently used, as a way to
    /// prevent having too many ETag changes, which put a bit of extra stress on downloaders.
    /// </remarks>
    public TimeSpan AllowHardTouchThreshold { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// The amount of time since the last touch before we force issue a write-touch which will cause the content's
    /// ETag to change.
    /// </summary>
    /// <remarks>
    /// This number was picked to ensure we keep the content with access times below 24h, which is when content might
    /// become eligible for deletion.
    ///
    /// Please note that between 12h and 22h, we'll touch with a probability that increases quadratically with the time
    /// that passed since 12h.
    /// </remarks>
    public TimeSpan ForceHardTouchThreshold { get; set; } = TimeSpan.FromHours(22);
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

    protected override Task<BoolResult> StartupComponentAsync(OperationContext context)
    {
        return _configuration.Topology.EnsureContainersExistAsync(context);
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
                StoreConfiguration: _configuration),
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
        return _configuration.Topology.GetClientWithPathAsync(context, contentHash);
    }
}
