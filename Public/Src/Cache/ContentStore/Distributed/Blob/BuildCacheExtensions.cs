// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Interfaces.Auth;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob
{
    public static class BuildCacheExtensions
    {
        public static bool TryGetAccountName(this BuildCacheShard shard, [NotNullWhen(true)] out BlobCacheStorageAccountName? accountName)
        {
            if (AzureStorageUtilities.TryGetAccountName(shard.StorageUri, out var rawAccountName))
            {
                accountName = BlobCacheStorageAccountName.Parse(rawAccountName);
                return true;
            }

            accountName = null;
            return false;
        }

        public static BlobCacheStorageAccountName GetAccountName(this BuildCacheShard shard)
        {
            return BlobCacheStorageAccountName.Parse(AzureStorageUtilities.GetAccountName(shard.StorageUri));
        }
    }
}
