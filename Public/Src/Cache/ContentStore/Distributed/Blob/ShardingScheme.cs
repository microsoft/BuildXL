// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

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
public record ShardingScheme(ShardingAlgorithm Scheme, List<BlobCacheStorageAccountName> Accounts)
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
                var accounts = new List<BlobCacheStorageAccountName>();

                // Non-sharding accounts go first, sorted by name. Sharding accounts go after.
                foreach (var group in Accounts.GroupBy(account => account is BlobCacheStorageShardingAccountName))
                {
                    if (group.Key)
                    {
                        accounts.AddRange(SortShardingAccounts(group.Cast<BlobCacheStorageShardingAccountName>().ToList()));
                    }
                    else
                    {
                        var temporary = SortUnshardedAccounts(group);
                        temporary.AddRange(accounts);
                        accounts = temporary;
                    }
                }

                return new JumpConsistentHash<BlobCacheStorageAccountName>(accounts);
            default:
                throw new ArgumentOutOfRangeException(paramName: nameof(Scheme), message: $"Unknown sharding scheme `{Scheme}`");
        }
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
}
