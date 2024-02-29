// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.ChangeFeed;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth;

/// <summary>
/// A helper base class that handles the blob related creation logic while abstracting away how credentials are retrieved.
/// </summary>
public abstract class AzureStorageCredentialsBase : IAzureStorageCredentials
{
    private static readonly Regex StorageAccountNameRegex = new("https://(?<accountName>[^\\.]+)\\.blob\\..*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Uri _blobUri;

    /// <summary>
    /// The <see cref="TokenCredential"/> to use for authenticating requests."/>
    /// </summary>
    protected abstract TokenCredential Credentials { get; }

    /// <nodoc />
    public AzureStorageCredentialsBase(Uri blobUri)
    {
        _blobUri = blobUri;
    }

    /// <inheritdoc />
    public string GetAccountName()
    {
        var match = StorageAccountNameRegex.Match(_blobUri.ToString());

        if (match.Success)
        {
            return match.Groups["accountName"].Value;
        }

        throw new InvalidOperationException($"The provided URI is malformed and the account name could not be retrieved.");
    }

    /// <inheritdoc />
    public BlobServiceClient CreateBlobServiceClient(BlobClientOptions? blobClientOptions = null)
        => new(_blobUri, Credentials, BlobClientOptionsFactory.CreateOrOverride(blobClientOptions));

    /// <inheritdoc />
    public BlobContainerClient CreateContainerClient(string containerName, BlobClientOptions? blobClientOptions = null)
        => new(new Uri(_blobUri, containerName), Credentials, BlobClientOptionsFactory.CreateOrOverride(blobClientOptions));

    /// <inheritdoc />
    public BlobChangeFeedClient CreateBlobChangeFeedClient(BlobClientOptions? blobClientOptions = null, BlobChangeFeedClientOptions? changeFeedClientOptions = null)
        => new BlobChangeFeedClient(_blobUri, Credentials, BlobClientOptionsFactory.CreateOrOverride(blobClientOptions), changeFeedClientOptions ?? new());
}
