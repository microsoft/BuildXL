// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Specifies a sharding algorithm to use.
/// </summary>
/// <remarks>
/// This enum does not cover all available sharding algorithms, just the ones that are meant to be used with the Blob
/// L3.
/// </remarks>
public enum ShardingAlgorithm
{
    /// <summary>
    /// Single shard.
    /// </summary>
    SingleShard,

    /// <summary>
    /// Multiple shards using <see cref="JumpConsistentHash{TShardId}"/>
    /// </summary>
    JumpHash,
}

/// <summary>
/// Specifies a sharding scheme.
/// </summary>
public record ShardingScheme(ShardingAlgorithm Scheme, IReadOnlyList<BlobCacheStorageAccountName> Accounts)
{
    public IShardingScheme<int, BlobCacheStorageAccountName> Create()
    {
        Contract.Requires(Accounts.Count > 0, $"Attempt to create an {nameof(ShardingScheme)} without any accounts");

        switch (Scheme)
        {
            case ShardingAlgorithm.SingleShard:
                Contract.Assert(Accounts.Count == 1, $"Requested using {nameof(ShardingAlgorithm.SingleShard)} sharding with more than 1 account");
                return new SingleShardingScheme<int, BlobCacheStorageAccountName>(Accounts[0]);
            case ShardingAlgorithm.JumpHash:
                return new JumpConsistentHash<BlobCacheStorageAccountName>(SortAccounts(Accounts));
            default:
                throw new ArgumentOutOfRangeException(paramName: nameof(Scheme), message: $"Unknown sharding scheme `{Scheme}`");
        }
    }

    public static IReadOnlyList<BlobCacheStorageAccountName> SortAccounts(IReadOnlyList<BlobCacheStorageAccountName> accounts)
    {
        var sorted = new List<BlobCacheStorageAccountName>();

        // Non-sharding accounts go first, sorted by name. Sharding accounts go after.
        foreach (var group in accounts.GroupBy(account => account is BlobCacheStorageShardingAccountName))
        {
            if (group.Key)
            {
                sorted.AddRange(SortShardingAccounts(group.Cast<BlobCacheStorageShardingAccountName>().ToList()));
            }
            else
            {
                var temporary = SortUnshardedAccounts(group);
                temporary.AddRange(sorted);
                sorted = temporary;
            }
        }

        return sorted;
    }

    private static List<BlobCacheStorageAccountName> SortUnshardedAccounts(IEnumerable<BlobCacheStorageAccountName> shards)
    {
        return shards.OrderBy(account => account.AccountName).ToList();
    }

    private static List<BlobCacheStorageAccountName> SortShardingAccounts(IReadOnlyCollection<BlobCacheStorageShardingAccountName> shards)
    {
        var purposes = shards.Select(account => account.Purpose).Distinct().ToList();
        if (purposes.Count != 1)
        {
            throw new ArgumentException($"Attempt to create an {nameof(ShardingScheme)} mixing different purposes");
        }

        return shards.OrderBy(account => account.ShardId).ToList<BlobCacheStorageAccountName>();
    }

    public (string Metadata, string Content) GenerateMatrix()
    {
        (long metadataSalt, long contentSalt) = GenerateSalt();

        return (
            Metadata: metadataSalt.ToString().Substring(0, 10),
            Content: contentSalt.ToString().Substring(0, 10));
    }

    internal (long Metadata, long Content) GenerateSalt()
    {
        // The matrix here ensures that metadata does not overlap across sharding schemes. Basically, whenever we add
        // or remove shards (or change the sharding algorithm), we will get a new salt. This salt will force us to use
        // a different matrix for metadata.
        //
        // Hence, sharding changes imply no metadata hits, but they do not imply no content hits. This is on
        // purpose because metadata hits guarantee content's existence, so we can't mess around with them.

        // Generate a stable hash out of the sharding scheme.
        var algorithm = (long)Scheme;
        var locations = Accounts.Select(location => HashCodeHelper.GetOrdinalIgnoreCaseHashCode64(location.AccountName)).ToArray();

        var algorithmSalt = HashCodeHelper.Combine(HashCodeHelper.Fnv1Basis64, algorithm);
        var locationsSalt = HashCodeHelper.Combine(locations);

        var metadataSalt = Math.Abs(HashCodeHelper.Combine(algorithmSalt, locationsSalt));

        // TODO: Ideally, we'd like the following to be algorithmSalt, but that would mean that GC needs to track the
        // salts differently for content and metadata.
        var contentSalt = metadataSalt;
        return (metadataSalt, contentSalt);
    }
}
