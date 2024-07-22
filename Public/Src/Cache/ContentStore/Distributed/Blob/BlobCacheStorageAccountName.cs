// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

[JsonConverter(typeof(BlobCacheStorageAccountNameJsonConverter))]
public abstract class BlobCacheStorageAccountName
{
    public string AccountName { get; }

    public BlobCacheStorageAccountName(string accountName)
    {
        Contract.Requires(LegacyBlobCacheContainerName.LowercaseAlphanumericRegex.IsMatch(accountName), $"{nameof(BlobCacheStorageNonShardingAccountName)} account names must be lowercase alphanumeric strings. Got {accountName} instead");
        AccountName = accountName;
    }

    public static BlobCacheStorageAccountName Parse(string input)
    {
        try
        {
            return BlobCacheStorageShardingAccountName.Parse(input);
        }
#pragma warning disable ERP022
        catch
        {
            return new BlobCacheStorageNonShardingAccountName(input);
        }
#pragma warning restore ERP022
    }

    public override int GetHashCode()
    {
        return AccountName.GetHashCode();
    }

    public override string ToString()
    {
        return AccountName;
    }

    public override bool Equals(object? obj)
    {
        if (obj is BlobCacheStorageAccountName other)
        {
            return string.Equals(AccountName, other.AccountName, StringComparison.InvariantCultureIgnoreCase);
        }

        return false;
    }

    public static bool operator ==(BlobCacheStorageAccountName? left, BlobCacheStorageAccountName? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(BlobCacheStorageAccountName? left, BlobCacheStorageAccountName? right)
    {
        return !(left == right);
    }
}

public sealed class BlobCacheStorageNonShardingAccountName : BlobCacheStorageAccountName
{
    public BlobCacheStorageNonShardingAccountName(string accountName)
        : base(accountName)
    {
    }
}

/// <summary>
/// This class imposes a naming scheme on storage accounts that are used for sharding. The reason for it is that we
/// need to be able to parse the account name to determine the shard id. The format is as follows:
///
/// {unique:10}{shard:5}{purpose:9}
///
/// - The unique token is a 10 character long string that is roughly unique globally. The purpose of it is to prevent
///   Azure Storage's range hashing causing two storage accounts to be in the same server and wind up having noisy
///   neighbor problems.
/// - The shard is a 5 digit number that is used to determine the shard id.
/// - The purpose is a string that is used to identify the purpose of the storage account. It is limited to 9
///   characters because the account name is limited to 24 characters as per the Azure Storage Account naming limits.
/// </summary>
public sealed class BlobCacheStorageShardingAccountName : BlobCacheStorageAccountName
{
    public string UniqueId { get; }

    public int ShardId { get; }

    public string Purpose { get; }

    public BlobCacheStorageShardingAccountName(string unique, int shard, string purpose)
        : base(CreateName(unique, shard, purpose))
    {
        UniqueId = unique;
        ShardId = shard;
        Purpose = purpose;
    }

    private static string CreateName(string unique, int shard, string purpose)
    {
        if (unique.Length != 10)
        {
            throw new FormatException(message: "Unique token should be of length 10");
        }

        if (!LegacyBlobCacheContainerName.LowercaseAlphanumericRegex.IsMatch(unique))
        {
            throw new FormatException(message: "Unique token should be composed of lower case numbers and letters");
        }

        if (shard is not (>= 0 and <= 99999))
        {
            throw new FormatException(message: "Shard is outside of valid range [0, 99999]. This is a requirement due to the naming convention");
        }

        if (purpose.Length is not (> 0 and <= 9))
        {
            throw new FormatException(message: "Purpose's length must be in (0, 9]");
        }

        if (!LegacyBlobCacheContainerName.LowercaseAlphanumericRegex.IsMatch(purpose))
        {
            throw new FormatException(message: "Unique token should be composed of lower case numbers and letters");
        }

        return $"{unique}{shard.ToString().PadLeft(5, '0')}{purpose}";
    }


    private static readonly Regex NameFormatRegex = new Regex(
        @"^(?<unique>[a-z0-9]{10})(?<shard>[0-9]{5})(?<purpose>[a-z0-9]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static new BlobCacheStorageShardingAccountName Parse(string input)
    {
        var match = NameFormatRegex.Match(input);
        if (!match.Success)
        {
            throw new FormatException(message: $"Failed to match {nameof(NameFormatRegex)} to {input}");
        }

        return new BlobCacheStorageShardingAccountName(match.Groups["unique"].Value, int.Parse(match.Groups["shard"].Value), match.Groups["purpose"].Value);
    }
}
