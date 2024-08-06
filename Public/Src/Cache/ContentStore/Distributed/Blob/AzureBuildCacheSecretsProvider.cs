// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using Azure;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Uses the information provided by 1ESHP related to an associated cache resource (<see cref="BuildCacheConfiguration"/>) in order to provide secrets
/// </summary>
public class AzureBuildCacheSecretsProvider : IBlobCacheContainerSecretsProvider
{
    private readonly ConcurrentDictionary<(BlobCacheStorageAccountName accountName, string containerName), IAzureStorageCredentials> _credentials = new();

    /// <nodoc/>
    public AzureBuildCacheSecretsProvider(BuildCacheConfiguration buildCacheConfiguration)
    {
        // Let's create all the secrets upfront. Each shard contains two relevant containers (content and metadata - we can ignore checkpoint for this)
        foreach (var shard in buildCacheConfiguration.Shards)
        {
            var accountName = shard.GetAccountName();
            addContainer(shard, accountName, shard.ContentContainer);
            addContainer(shard, accountName, shard.MetadataContainer);
            addContainer(shard, accountName, shard.CheckpointContainer);
        }

        void addContainer(BuildCacheShard shard, BlobCacheStorageAccountName accountName, BuildCacheContainer container)
        {
            var sasCredential = new AzureSasCredential(container.Signature);
            _credentials.TryAdd((accountName, container.Name), new ContainerSasStorageCredentials(shard.StorageUrl, container.Name, sasCredential));
        }
    }

    /// <inheritdoc/>
    public Task<IAzureStorageCredentials> RetrieveContainerCredentialsAsync(OperationContext context, BlobCacheStorageAccountName account, BlobCacheContainerName container)
    {
        if (!_credentials.TryGetValue((account, container.ContainerName), out var credentials))
        {
            Contract.Assert(false, $"Could not find a credential for {account.AccountName} and {container.ContainerName}");
        }

        return Task.FromResult(credentials);
    }
}
