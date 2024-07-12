// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// The naming scheme here captures the concept that the actual container names used is version dependent. This is
/// useful in the context of different permission levels being granted to the credentials being used to access a
/// storage account.
/// </summary>
public abstract class ContainerNamingScheme
{
    /// <summary>
    /// Generate naming schemes for containers/>.
    /// </summary>
    public abstract IReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]> GenerateContainerNameMapping();
}

public abstract class ContainerNamingSchemeBase<TKey> : ContainerNamingScheme
{ 
    /// <summary>
    /// Returns the container name associated with a given key and purpose
    /// </summary>
    protected abstract BlobCacheContainerName GetContainerName(TKey key, BlobCacheContainerPurpose purpose);

    /// <summary>
    /// Returns the containers associated with the given key
    /// </summary>
    protected BlobCacheContainerName[] GetContainers(TKey key)
    {
        return Enum.GetValues(typeof(BlobCacheContainerPurpose)).Cast<BlobCacheContainerPurpose>().Select(
                purpose =>
                    purpose switch
                    {
                        BlobCacheContainerPurpose.Content => GetContainerName(key, purpose),
                        BlobCacheContainerPurpose.Metadata => GetContainerName(key, purpose),
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(purpose),
                            purpose,
                            $"Unknown value for {nameof(BlobCacheContainerPurpose)}: {purpose}"),
                    }
                ).ToArray();
    }
}

public class LegacyContainerNamingScheme : ContainerNamingSchemeBase<(string metadata, string content)>
{
    private readonly IReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]> _shardsPerAccountName;
    private readonly string _universe;
    private readonly string _namespace;

    public LegacyContainerNamingScheme(ShardingScheme scheme, string universe, string @namespace)
    {
        _universe = universe;
        _namespace = @namespace;

        _shardsPerAccountName = new ReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]>(
            scheme.Accounts.ToDictionary(
                account => account,
                account => GetContainers(scheme.GenerateMatrix())));
    }

    public override IReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]> GenerateContainerNameMapping() => _shardsPerAccountName;

    protected override BlobCacheContainerName GetContainerName((string metadata, string content) matrices, BlobCacheContainerPurpose purpose)
    {
        return new LegacyBlobCacheContainerName(
            purpose,
            purpose switch {
                BlobCacheContainerPurpose.Content => matrices.content,
                BlobCacheContainerPurpose.Metadata => matrices.metadata,
                _ => throw new ArgumentOutOfRangeException(
                            nameof(purpose),
                            purpose,
                            $"Unknown value for {nameof(BlobCacheContainerPurpose)}: {purpose}"),
            },
            _universe,
            _namespace);
    }
}

public class BuildCacheContainerNamingScheme : ContainerNamingSchemeBase<BuildCacheShard>
{
    private readonly IReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]> _shardsPerAccountName;

    public BuildCacheContainerNamingScheme(BuildCacheConfiguration buildCacheConfiguration)
    {
        _shardsPerAccountName = new ReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]>(
            buildCacheConfiguration.Shards.ToDictionary(
                shard => (BlobCacheStorageAccountName) new BlobCacheStorageNonShardingAccountName(shard.StorageUri.AbsoluteUri),
                shard => GetContainers(shard)));
    }

    public override IReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]> GenerateContainerNameMapping() => _shardsPerAccountName;

    protected override BlobCacheContainerName GetContainerName(BuildCacheShard shard, BlobCacheContainerPurpose purpose)
    {
        return new FixedCacheBlobContainerName(
            purpose switch
            {
                BlobCacheContainerPurpose.Content => shard.ContentContainer.Name,
                BlobCacheContainerPurpose.Metadata => shard.MetadataContainer.Name,
                _ => throw new ArgumentOutOfRangeException(
                            nameof(purpose),
                            purpose,
                            $"Unknown value for {nameof(BlobCacheContainerPurpose)}: {purpose}"),
            },
            purpose);
    }
}
