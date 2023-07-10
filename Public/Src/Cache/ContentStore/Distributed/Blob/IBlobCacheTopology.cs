// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Obtains a client for a given sharding key. The intent is that the sharding key is used to determine which storage
/// account to use. The implementor is free to decide how to perform the mapping, but the resulting client should be
/// usable to access the container (i.e., there shouldn't be permission issues, the container should exist, etc).
/// </summary>
public interface IBlobCacheTopology
{
    public Task<BlobContainerClient> GetContainerClientAsync(OperationContext context, BlobCacheShardingKey key);

    public Task<BlobClient> GetBlobClientAsync(OperationContext context, ContentHash hash);

    public IAsyncEnumerable<BlobContainerClient> EnumerateClientsAsync(OperationContext context, BlobCacheContainerPurpose purpose);
}
