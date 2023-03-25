// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// This class is meant to allow user of the Blob L3 to provide credentials in a plug-in model.
/// </summary>
public interface IBlobCacheSecretsProvider
{
    /// <summary>
    /// Requests credentials to Azure Storage.
    /// </summary>
    public Task<AzureStorageCredentials> RetrieveBlobCredentialsAsync(
        OperationContext context,
        BlobCacheStorageAccountName account,
        BlobCacheContainerName container);
}
