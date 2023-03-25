// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Abstraction over sharding keys. This is needed because sharding schemes need a uniformly typed key that they
/// can map to a shard. This is the type that bridges "I need a shard for this thing" with "this is something I can
/// obtain a shard with (int)".
/// </summary>
/// <remarks>
/// It is EXTREMELY important that the hash code for these not change across processes. All processes in a distributed
/// system MUST emit the same Key for the same object. Otherwise, the guarantee that two clients will find the key in
/// the same node is lost.
/// </remarks>
public readonly record struct BlobCacheShardingKey(BlobCacheContainerPurpose Purpose, int Key)
{
    public static BlobCacheShardingKey FromContentHash(ContentHash contentHash)
    {
        return new BlobCacheShardingKey(Purpose: BlobCacheContainerPurpose.Content, Key: contentHash.GetHashCode());
    }

    public static BlobCacheShardingKey FromWeakFingerprint(Fingerprint fingerprint)
    {
        return new BlobCacheShardingKey(Purpose: BlobCacheContainerPurpose.Metadata, Key: fingerprint.GetHashCode());
    }
}
