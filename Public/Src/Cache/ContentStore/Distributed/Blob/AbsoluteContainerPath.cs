// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.Blob;

public readonly record struct AbsoluteContainerPath(BlobCacheStorageAccountName Account, BlobCacheContainerName Container)
{
    public override string ToString()
    {
        return $"{Account}/{Container}";
    }
}
