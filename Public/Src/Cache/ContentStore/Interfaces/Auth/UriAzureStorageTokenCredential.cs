// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.Core;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth;

/// <summary>
/// Provides credentials for a given <see cref="Uri"/> based on a explicitly provided <see cref="TokenCredential"/>"/>
/// </summary>
public class UriAzureStorageTokenCredential : AzureStorageCredentialsBase
{
    /// <inheritdoc/>
    protected override TokenCredential Credentials { get; }

    /// <nodoc />
    public UriAzureStorageTokenCredential(TokenCredential tokenCredential, Uri blobUri) : base(blobUri)
    {
        Credentials = tokenCredential;
    }
}
