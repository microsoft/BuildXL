// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Convenience class for users to provide credentials from memory.
/// </summary>
public class StaticBlobCacheSecretsProvider : IBlobCacheAccountSecretsProvider
{
    protected static Tracer Tracer { get; } = new(nameof(StaticBlobCacheSecretsProvider));

    public IReadOnlyList<BlobCacheStorageAccountName> ConfiguredAccounts => _accounts;

    private readonly IAzureStorageCredentials? _fallback;
    private readonly IReadOnlyDictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> _credentials = new Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials>();
    private readonly IReadOnlyList<BlobCacheStorageAccountName> _accounts;

    public StaticBlobCacheSecretsProvider(IReadOnlyDictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> credentials, IAzureStorageCredentials? fallback = null)
    {
        _credentials = credentials;
        _accounts = _credentials.Keys.ToArray();
        _fallback = fallback;
    }

    public StaticBlobCacheSecretsProvider(IAzureStorageCredentials fallback)
    {
        _fallback = fallback;
        _accounts = _credentials.Keys.ToArray();
    }

    public Task<IAzureStorageCredentials> RetrieveAccountCredentialsAsync(OperationContext context, BlobCacheStorageAccountName account)
    {
        Tracer.Info(context, $"Fetching credentials. Account=[{account}]");

        if (_credentials.TryGetValue(account, out var credentials))
        {
            return Task.FromResult(credentials);
        }

        if (_fallback is not null)
        {
            return Task.FromResult(_fallback);
        }

        throw new KeyNotFoundException($"Credentials are unavailable for storage account {account}");
    }

    public Task<IAzureStorageCredentials> RetrieveContainerCredentialsAsync(
        OperationContext context,
        BlobCacheStorageAccountName account,
        BlobCacheContainerName container)
    {
        // Account credentials are sufficient to access containers, but the other way around isn't true.
        return RetrieveAccountCredentialsAsync(context, account);
    }

}
