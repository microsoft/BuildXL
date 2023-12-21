// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Storage.Blobs;
using BuildXL.Cache.ContentStore.Interfaces.Auth;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// This class works as a wrapper around the Azure Storage SDK to hand out clients that are "folder-scoped"
/// </summary>
public class AzureBlobStorageFolder
{
    public record Configuration(IAzureStorageCredentials Credentials, string ContainerName, string? FolderName)
    {
        public AzureBlobStorageFolder Create()
        {
            return new AzureBlobStorageFolder(this);
        }
    }

    private readonly Configuration _configuration;

    private AzureBlobStorageFolder(Configuration configuration)
    {
        _configuration = configuration;
    }

    public BlobServiceClient GetServiceClient()
    {
        return _configuration.Credentials.CreateBlobServiceClient();
    }

    public BlobContainerClient GetContainerClient(BlobServiceClient? client = null)
    {
        client ??= GetServiceClient();
        return client.GetBlobContainerClient(_configuration.ContainerName);
    }

    public BlobClient GetBlobClient(BlobServiceClient client, BlobPath path)
    {
        return GetBlobClient(GetContainerClient(client), path);
    }

    public BlobClient GetBlobClient(BlobContainerClient client, BlobPath path)
    {
        return client.GetBlobClient(GetBlobName(path));
    }

    public BlobClient GetBlobClient(BlobPath path)
    {
        return GetBlobClient(GetContainerClient(), path);
    }

    private string GetBlobName(BlobPath path)
    {
        return !string.IsNullOrEmpty(_configuration.FolderName) && path.IsRelative ? $"{_configuration.FolderName}/{path}" : path.Path;
    }

    public string? FolderPrefix => string.IsNullOrEmpty(_configuration.FolderName) ? null : $"{_configuration.FolderName}/";
}
