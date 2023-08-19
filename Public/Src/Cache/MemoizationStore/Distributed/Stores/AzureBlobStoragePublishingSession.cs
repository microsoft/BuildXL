// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Stores;

/// <nodoc />
public class AzureBlobStoragePublishingSessionConfiguration
{
    /// <nodoc />
    public AzureBlobStorageCacheFactory.Configuration Configuration { get; set; }

    /// <nodoc />
    public string SessionName { get; set; }

    /// <nodoc />
    public string PersonalAccessToken { get; set; }

    /// <nodoc />
    public AzureBlobStoragePublishingStore Parent { get; set; }
}

/// <nodoc />
public class AzureBlobStoragePublishingSession : PublishingSessionBase<AzureBlobStoragePublishingSessionConfiguration>
{
    /// <nodoc />
    protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStoragePublishingSession));

    /// <nodoc />
    public AzureBlobStoragePublishingSession(
        AzureBlobStoragePublishingSessionConfiguration configuration,
        Func<IContentSession> localContentSessionFactory,
        SemaphoreSlim fingerprintPublishingGate,
        SemaphoreSlim contentPublishingGate)
        : base(
            configuration,
            localContentSessionFactory,
            fingerprintPublishingGate,
            contentPublishingGate)
    {
    }

    /// <nodoc />
    protected override async Task<ICachePublisher> CreateCachePublisherCoreAsync(
        OperationContext context,
        AzureBlobStoragePublishingSessionConfiguration configuration)
    {
        // TODO: force publishing for blob l3
        // TODO: how to deal with sharded secrets in async publishing?
        var secretsProvider = new StaticBlobCacheSecretsProvider(fallback: new SecretBasedAzureStorageCredentials(connectionString: configuration.PersonalAccessToken));
        var cache = AzureBlobStorageCacheFactory.Create(configuration.Configuration, secretsProvider);
        await cache.StartupAsync(context).ThrowIfFailureAsync();
        var session = ((ICache)cache).CreateSession(context, name: configuration.SessionName, implicitPin: ImplicitPin.None).ThrowIfFailure();

        return new CacheSessionPublisherWrapper(cache, session.Session);
    }
}
