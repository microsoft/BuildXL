// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.Core;
using Azure.Identity;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth;

/// <nodoc />
public class ManagedIdentityAzureStorageCredentials : AzureStorageCredentialsBase
{
    private readonly ManagedIdentityCredential _credentials;

    /// <summary>
    /// Creates credentials using a user-assigned managed identity.
    /// </summary>
    /// <param name="managedIdentityClientId">The client ID of the user-assigned managed identity.</param>
    /// <param name="blobUri">The URI of the blob storage account.</param>
    public ManagedIdentityAzureStorageCredentials(string managedIdentityClientId, Uri blobUri) : base(blobUri)
    {
        _credentials = new ManagedIdentityCredential(managedIdentityClientId);
    }

    /// <summary>
    /// Creates credentials using the system-assigned managed identity of the hosting environment
    /// (Azure VM, Azure Arc-enabled server, etc.). When no client ID is provided,
    /// <see cref="ManagedIdentityCredential"/> automatically discovers the system-assigned identity
    /// via the local metadata service (IMDS).
    /// </summary>
    /// <param name="blobUri">The URI of the blob storage account.</param>
    public ManagedIdentityAzureStorageCredentials(Uri blobUri) : base(blobUri)
    {
        _credentials = new ManagedIdentityCredential();
    }

    /// <inheritdoc/>
    protected override TokenCredential Credentials => _credentials;
}
