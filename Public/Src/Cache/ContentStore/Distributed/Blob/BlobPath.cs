// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Diagnostics.ContractsLight;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Represents a full or relative blob path
/// </summary>
public readonly record struct BlobPath
{
    public string Path { get; init; }

    public bool IsRelative { get; init; }

    public BlobPath(string path, bool relative)
    {
        Contract.RequiresNotNullOrWhiteSpace(path);
        Path = path;
        IsRelative = relative;
    }

    public static BlobPath CreateAbsolute(string path) => new(path, relative: false);

    public override string ToString()
    {
        return Path;
    }

    public static implicit operator BlobPath(BlobClient blob)
    {
        return new BlobPath(blob.Name, relative: false);
    }

    public static implicit operator BlobPath(BlobItem blob)
    {
        return new BlobPath(blob.Name, relative: false);
    }
}
