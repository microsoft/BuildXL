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

    /// <nodoc />
    public ManagedIdentityAzureStorageCredentials(string managedIdentityClientId, Uri blobUri) : base(blobUri)
    {
        _credentials = new ManagedIdentityCredential(managedIdentityClientId);
    }

    /// <inheritdoc/>
    protected override TokenCredential Credentials => _credentials;
}
