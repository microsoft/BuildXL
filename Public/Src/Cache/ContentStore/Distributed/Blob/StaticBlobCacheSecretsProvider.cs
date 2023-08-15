// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Convenience class for users to provide credentials from memory.
/// </summary>
public class StaticBlobCacheSecretsProvider : IBlobCacheSecretsProvider
{
    protected static Tracer Tracer { get; } = new(nameof(StaticBlobCacheSecretsProvider));

    public IReadOnlyList<BlobCacheStorageAccountName> ConfiguredAccounts => _accounts;

    private readonly AzureStorageCredentials? _fallback;
    private readonly IReadOnlyDictionary<BlobCacheStorageAccountName, AzureStorageCredentials> _credentials = new Dictionary<BlobCacheStorageAccountName, AzureStorageCredentials>();
    private readonly IReadOnlyList<BlobCacheStorageAccountName> _accounts;

    public StaticBlobCacheSecretsProvider(IReadOnlyDictionary<BlobCacheStorageAccountName, AzureStorageCredentials> credentials, AzureStorageCredentials? fallback = null)
    {
        _credentials = credentials;
        _accounts = _credentials.Keys.ToArray();
        _fallback = fallback;
    }

    public StaticBlobCacheSecretsProvider(AzureStorageCredentials fallback)
    {
        _fallback = fallback;
        _accounts = _credentials.Keys.ToArray();
    }

    public Task<AzureStorageCredentials> RetrieveBlobCredentialsAsync(OperationContext context, BlobCacheStorageAccountName account)
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
}
