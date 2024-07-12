// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Uses the information provided by 1ESHP related to an associated cache resource (<see cref="BuildCacheConfiguration"/>) in order to provide secrets
/// </summary>
public class AzureBuildCacheSecretsProvider : IBlobCacheContainerSecretsProvider
{
    private readonly ConcurrentDictionary<(string accountName, string containerName), IAzureStorageCredentials> _credentials = new();

    /// <nodoc/>
    public AzureBuildCacheSecretsProvider(BuildCacheConfiguration buildCacheConfiguration)
    {
        // Let's create all the secrets upfront. Each shard contains two relevant containers (content and metadata - we can ignore checkpoint for this)
        foreach (var shard in buildCacheConfiguration.Shards)
        {
            var contentContainer = shard.ContentContainer;
            var metadataContainer = shard.MetadataContainer;

            
            _credentials.TryAdd((shard.StorageUri.AbsoluteUri, contentContainer.Name), new ContainerSasStorageCredentials(contentContainer.SasUrl));
            _credentials.TryAdd((shard.StorageUri.AbsoluteUri, metadataContainer.Name), new ContainerSasStorageCredentials(metadataContainer.SasUrl));
        }
    }

    /// <inheritdoc/>
    public Task<IAzureStorageCredentials> RetrieveContainerCredentialsAsync(OperationContext context, BlobCacheStorageAccountName account, BlobCacheContainerName container)
    {
        if (!_credentials.TryGetValue((account.AccountName, container.ContainerName), out var credentials))
        {
            Contract.Assert(false, $"Could not find a credential for {account.AccountName} and {container.ContainerName}");
        }

        return Task.FromResult(credentials);
    }
}
