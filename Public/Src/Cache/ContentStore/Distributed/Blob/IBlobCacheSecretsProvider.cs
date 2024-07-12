// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// This class is meant to allow user of the Blob L3 to provide credentials in a plug-in model.
/// </summary>
/// <remarks>
/// Assumptions:
/// - Implementors of this interface are expected to provide credentials that are sufficient to access the container
/// in a RW mode.
/// - The credentials are expected to be valid for the lifetime of the application.
/// </remarks>
public interface IBlobCacheContainerSecretsProvider
{
    /// <summary>
    /// Requests credentials to Azure Storage.
    /// </summary>
    public Task<IAzureStorageCredentials> RetrieveContainerCredentialsAsync(
        OperationContext context,
        BlobCacheStorageAccountName account,
        BlobCacheContainerName container);
}

/// <summary>
/// This class is meant to allow user of the Blob L3 to provide credentials in a plug-in model.
/// </summary>
/// <remarks>
/// Assumptions:
/// - Implementors of this interface are expected to provide credentials that are sufficient to access all containers in
/// the storage account in a RW mode, they may also access the blob change feed, and may optionally delete containers.
/// </remarks>
public interface IBlobCacheAccountSecretsProvider : IBlobCacheContainerSecretsProvider
{
    /// <summary>
    /// Requests credentials to Azure Storage.
    /// </summary>
    public Task<IAzureStorageCredentials> RetrieveAccountCredentialsAsync(
        OperationContext context,
        BlobCacheStorageAccountName account);
}

