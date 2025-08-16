// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Obtains a client for a given sharding key. The intent is that the sharding key is used to determine which storage
/// account to use. The implementor is free to decide how to perform the mapping, but the resulting client should be
/// usable to access the container (i.e., there shouldn't be permission issues, the container should exist, etc).
/// </summary>
public interface IBlobCacheTopology
{
    /// <summary>
    /// Gets the container client for a given sharding key.
    /// </summary>
    /// <remarks>
    /// You likely want to use <see cref="BlobCacheTopologyExtensions"/>'s methods instead of calling this directly.
    /// </remarks>
    public Task<(BlobContainerClient Client, AbsoluteContainerPath Path)> GetShardContainerClientWithPathAsync(OperationContext context, BlobCacheShardingKey key);

    public IEnumerable<AbsoluteContainerPath> EnumerateContainers(OperationContext context, BlobCacheContainerPurpose purpose);

    public IAsyncEnumerable<(BlobContainerClient Client, AbsoluteContainerPath Path)> EnumerateClientsAsync(OperationContext context, BlobCacheContainerPurpose purpose);

    public Task<BoolResult> EnsureContainersExistAsync(OperationContext context);

    public Task<Result<AzureSasCredential>> GetBlobContainerPreauthenticatedSasTokenAsync(OperationContext context, BlobCacheShardingKey key);
}

public static class BlobCacheTopologyExtensions
{
    public static async Task<BlobClient> GetClientAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        ContentHash contentHash)
    {
        var containerClient = await GetContainerClientAsync(topology, context, contentHash);
        var blobPath = BlobPath.CreateAbsolute($"{contentHash}.blob");
        var blobClient = containerClient.GetBlobClient(blobPath.Path);
        return blobClient;
    }

    public static async Task<BlobClient> GetClientAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        StrongFingerprint strongFingerprint)
    {
        var containerClient = await GetContainerClientAsync(topology, context, strongFingerprint);
        var blobPath = BlobPath.CreateAbsolute(GetStrongFingerprintBlobPath(strongFingerprint));
        var blobClient = containerClient.GetBlobClient(blobPath.Path);
        return blobClient;
    }

    public static async Task<(BlobClient Client, AbsoluteBlobPath Path)> GetClientWithPathAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        ContentHash contentHash)
    {
        var (containerClient, containerPath) = await GetContainerClientWithPathAsync(topology, context, contentHash);
        var blobPath = BlobPath.CreateAbsolute($"{contentHash}.blob");
        var blobClient = containerClient.GetBlobClient(blobPath.Path);
        var absoluteBlobPath = new AbsoluteBlobPath(containerPath, blobPath);
        return (blobClient, absoluteBlobPath);
    }

    public static async Task<(BlobClient Client, AbsoluteBlobPath Path)> GetClientWithPathAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        StrongFingerprint strongFingerprint)
    {
        var (containerClient, containerPath) = await GetContainerClientWithPathAsync(topology, context, strongFingerprint);
        var blobPath = BlobPath.CreateAbsolute(GetStrongFingerprintBlobPath(strongFingerprint));
        var absoluteBlobPath = new AbsoluteBlobPath(containerPath, blobPath);
        var blobClient = containerClient.GetBlobClient(blobPath.Path);
        return (blobClient, absoluteBlobPath);
    }

    public static async Task<BlobContainerClient> GetContainerClientAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        ContentHash contentHash)
    {
        var shardingKey = BlobCacheShardingKey.FromContentHash(contentHash);
        var (containerClient, containerPath) = await topology.GetShardContainerClientWithPathAsync(context, shardingKey);
        return containerClient;
    }

    public static async Task<BlobContainerClient> GetContainerClientAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        Fingerprint weakFingerprint)
    {
        var shardingKey = BlobCacheShardingKey.FromWeakFingerprint(weakFingerprint);
        var (containerClient, containerPath) = (await topology.GetShardContainerClientWithPathAsync(context, shardingKey));
        return containerClient;
    }


    public static Task<BlobContainerClient> GetContainerClientAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        StrongFingerprint strongFingerprint)
    {
        return topology.GetContainerClientAsync(context, strongFingerprint.WeakFingerprint);
    }


    public static async Task<(BlobContainerClient Client, AbsoluteContainerPath Path)> GetContainerClientWithPathAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        ContentHash contentHash)
    {
        var shardingKey = BlobCacheShardingKey.FromContentHash(contentHash);
        var (containerClient, containerPath) = await topology.GetShardContainerClientWithPathAsync(context, shardingKey);
        return (containerClient, containerPath);
    }

    public static Task<(BlobContainerClient Client, AbsoluteContainerPath Path)> GetContainerClientWithPathAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        Fingerprint weakFingerprint)
    {
        var shardingKey = BlobCacheShardingKey.FromWeakFingerprint(weakFingerprint);
        return topology.GetShardContainerClientWithPathAsync(context, shardingKey);
    }


    public static Task<(BlobContainerClient Client, AbsoluteContainerPath Path)> GetContainerClientWithPathAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        StrongFingerprint strongFingerprint)
    {
        return topology.GetContainerClientWithPathAsync(context, strongFingerprint.WeakFingerprint);
    }

    public static Task<Result<AzureSasCredential>> GetBlobContainerPreauthenticatedSasTokenAsync(
        this IBlobCacheTopology topology,
        OperationContext context,
        ContentHash contentHash)
    {
        var shardingKey = BlobCacheShardingKey.FromContentHash(contentHash);
        return topology.GetBlobContainerPreauthenticatedSasTokenAsync(context, shardingKey);
    }

    public static string GetWeakFingerprintPrefix(Fingerprint weakFingerprint)
    {
        return weakFingerprint.Serialize();
    }

    /// <summary>
    /// CODESYNC: <see cref="ExtractStrongFingerprintFromPath(string)"/> should reflect any changes in how we serialize the blob path.
    /// </summary>
    public static string GetStrongFingerprintBlobPath(StrongFingerprint strongFingerprint)
    {
        var selector = strongFingerprint.Selector;

        // WARNING: the serialization format that follows must sync with _selectorRegex
        var contentHashName = selector.ContentHash.Serialize();

        var selectorName = selector.Output is null
            ? $"{contentHashName}"
            : $"{contentHashName}_{Convert.ToBase64String(selector.Output, Base64FormattingOptions.None)}";

        // WARNING: the policy on blob naming complicates things. A blob name must not be longer than 1024
        // characters long.
        // See: https://learn.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata#blob-names
        var blobPath = $"{GetWeakFingerprintPrefix(strongFingerprint.WeakFingerprint)}/{selectorName}";
        return blobPath;
    }

    /// <nodoc />
    public static StrongFingerprint ExtractStrongFingerprintFromPath(string blobPath)
    {
        var match = SelectorRegex.Match(blobPath);
        if (!match.Success)
        {
            throw new Exception($"Regex was not a match for path {blobPath}");
        }

        var serializedWeakFingerprint = match.Groups["weakFingerprint"].Value;
        if (!Fingerprint.TryParse(serializedWeakFingerprint, out var weakFingerprint))
        {
            throw new Exception($"Failed to parse weak fingerprint from {serializedWeakFingerprint}. Full path: {blobPath}");
        }

        var serializedSelectorContentHash = match.Groups["selectorContentHash"].Value;
        if (!ContentHash.TryParse(serializedSelectorContentHash, out var contentHash))
        {
            throw new Exception($"Failed to parse content hash from {serializedSelectorContentHash}. Full path: {blobPath}");
        }

        var serializedSelectorOutput = match.Groups["selectorOutput"].Value;
        byte[]? selectorOutput = null;
        if (!string.IsNullOrEmpty(serializedSelectorOutput))
        {
            selectorOutput = Convert.FromBase64String(serializedSelectorOutput);
        }

        return new StrongFingerprint(weakFingerprint, new Selector(contentHash, selectorOutput));
    }

    /// <summary>
    /// WARNING: MUST SYNC WITH <see cref="GetStrongFingerprintClientAsync(OperationContext, StrongFingerprint)"/>
    /// </summary>
    private static readonly Regex SelectorRegex = new Regex(@"(?<weakFingerprint>[A-Z0-9]+)/(?<selectorContentHash>[^_]+)(?:_(?<selectorOutput>.*))?");

    public static Selector ExtractSelectorFromPath(BlobPath name)
    {
        try
        {
            var match = SelectorRegex.Match(name.Path);

            var contentHash = new ContentHash(match.Groups["selectorContentHash"].Value);

            // The output can be null, empty, or something else. This is important because we need to ensure that
            // the user reads whatever they wrote in the first place.
            var outputGroup = match.Groups["selectorOutput"];
            var selectorOutput = outputGroup.Success ? outputGroup.Value : null;
            var output = selectorOutput is null ? null : Convert.FromBase64String(selectorOutput);

            return new Selector(contentHash, output);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed parsing a {nameof(Selector)} out of '{name}'", paramName: nameof(name), ex);
        }
    }
}
