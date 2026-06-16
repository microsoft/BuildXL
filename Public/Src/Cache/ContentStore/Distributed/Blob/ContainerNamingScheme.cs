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
/// Specifies explicit container names to use instead of the computed naming scheme.
/// All three container names must be provided together.
/// </summary>
public record ContainerNameOverrideConfig(
    string Content,
    string Metadata,
    string Checkpoint);

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
                        BlobCacheContainerPurpose.Checkpoint => GetContainerName(key, purpose),
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
                BlobCacheContainerPurpose.Checkpoint => "checkpoint",
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
                shard => shard.GetAccountName(),
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
                BlobCacheContainerPurpose.Checkpoint => shard.CheckpointContainer.Name,
                _ => throw new ArgumentOutOfRangeException(
                            nameof(purpose),
                            purpose,
                            $"Unknown value for {nameof(BlobCacheContainerPurpose)}: {purpose}"),
            },
            purpose);
    }
}

/// <summary>
/// A naming scheme that uses explicitly configured container names instead of computing them.
/// Useful when containers are pre-created and the identity lacks container-create permissions.
/// </summary>
/// <remarks>
/// Unlike <see cref="LegacyContainerNamingScheme"/>, this scheme does not incorporate the shard
/// topology into the container names. With <see cref="LegacyContainerNamingScheme"/>, the container
/// names include a matrix hash derived from the account names, so changing shards automatically
/// produces new container names — forcing a clean cache miss and avoiding stale metadata lookups.
///
/// With this scheme, if the shard topology changes (accounts added or removed) but the same container
/// names are kept, old metadata remains reachable. That metadata references content stored under the
/// old JumpHash distribution, but JumpHash with a different shard count routes lookups to different
/// shards. With PinCachedOutputs=true (the default), BuildXL detects the missing content during the
/// availability check and treats it as a cache miss — the pip re-executes and the build succeeds,
/// but with degraded performance due to wasted metadata lookups. If PinCachedOutputs is disabled,
/// the content check is skipped and materialization will fail, causing a build error. When resharding,
/// callers should create new containers with new names and update the configuration accordingly.
/// </remarks>
public class ConfiguredContainerNamingScheme : ContainerNamingSchemeBase<object?>
{
    private readonly string _contentContainerName;
    private readonly string _metadataContainerName;
    private readonly string _checkpointContainerName;
    private readonly IReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]> _mapping;

    public ConfiguredContainerNamingScheme(
        IReadOnlyList<BlobCacheStorageAccountName> accounts,
        string contentContainerName,
        string metadataContainerName,
        string checkpointContainerName)
    {
        _contentContainerName = contentContainerName;
        _metadataContainerName = metadataContainerName;
        _checkpointContainerName = checkpointContainerName;

        _mapping = new ReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]>(
            accounts.ToDictionary(
                account => account,
                account => GetContainers(null)));
    }

    public override IReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]> GenerateContainerNameMapping() => _mapping;

    protected override BlobCacheContainerName GetContainerName(object? key, BlobCacheContainerPurpose purpose)
    {
        return new FixedCacheBlobContainerName(
            purpose switch
            {
                BlobCacheContainerPurpose.Content => _contentContainerName,
                BlobCacheContainerPurpose.Metadata => _metadataContainerName,
                BlobCacheContainerPurpose.Checkpoint => _checkpointContainerName,
                _ => throw new ArgumentOutOfRangeException(
                            nameof(purpose),
                            purpose,
                            $"Unknown value for {nameof(BlobCacheContainerPurpose)}: {purpose}"),
            },
            purpose);
    }
}
