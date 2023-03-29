// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Abstraction over sharding keys. This is needed because sharding schemes need a uniformly typed hash that they
/// can map to a shard. This is the type that bridges "I need a shard for this thing" with "this is something I can
/// obtain a shard with (int)".
/// </summary>
/// <remarks>
/// It is EXTREMELY important that the hash code for these not change across processes. All processes in a distributed
/// system MUST emit the same Key for the same object. Otherwise, the guarantee that two clients will find the hash in
/// the same node is lost.
/// </remarks>
public readonly record struct BlobCacheShardingKey(BlobCacheContainerPurpose Purpose, int Key)
{
    public static BlobCacheShardingKey FromContentHash(ContentHash contentHash)
    {
        var typeHash = (int)contentHash.HashType;
        var byteHash = GetStableHash(contentHash.ToFixedBytes());
        var hash = HashCodeHelper.Fold(byteHash, typeHash);

        return new BlobCacheShardingKey(Purpose: BlobCacheContainerPurpose.Content, Key: hash);
    }

    public static BlobCacheShardingKey FromWeakFingerprint(Fingerprint fingerprint)
    {
        var hash = GetStableHash(fingerprint.ToFixedBytes());
        return new BlobCacheShardingKey(Purpose: BlobCacheContainerPurpose.Metadata, Key: hash);
    }

    public static int GetStableHash(ReadOnlyFixedBytes bytes)
    {
        return HashCodeHelper.Combine(bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7], bytes[8]);
    }
}
