// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

public class ShardedBlobCacheTopology : IBlobCacheTopology
{
    protected Tracer Tracer { get; } = new Tracer(nameof(ShardedBlobCacheTopology));

    public record BlobRetryPolicy
    {
        /// <summary>
        /// Maximum number of retries for Azure Storage client.
        /// </summary>
        public int MaxRetries { get; set; } = 20;
        /// <summary>
        /// Delay for Azure Storage client.
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(0.5);
        /// <summary>
        /// Maximum amount of time we're willing to wait for any operation against storage.
        /// </summary>
        public TimeSpan NetworkTimeout { get; set; } = TimeSpan.FromSeconds(200);
    }

    public record Configuration(
        ShardingScheme ShardingScheme,
        IBlobCacheSecretsProvider SecretsProvider,
        string Universe,
        string Namespace,
        BlobRetryPolicy BlobRetryPolicy,
        TimeSpan? ClientCreationTimeout = null);

    private readonly Configuration _configuration;

    private readonly BlobClientOptions _blobClientOptions;

    /// <summary>
    /// Holds pre-allocated container names to avoid allocating strings every time we want to get the container for a
    /// given key.
    /// </summary>
    private readonly BlobCacheContainerName[] _containers;
    private readonly IShardingScheme<int, BlobCacheStorageAccountName> _scheme;

    /// <summary>
    /// Used to implement a double-checked locking pattern at the per-container level. Essentially, we don't want to
    /// waste resources by creating clients for the same container at the same time.
    /// </summary>
    private readonly LockSet<AbsoluteContainerPath> _locks = new();

    /// <summary>
    /// We cache the clients because:
    /// 1. Obtaining clients requires obtaining storage credentials, which may or may not involve RPCs.
    /// 2. Once the storage credential has been obtained, we should be fine re-using it.
    /// 3. It is possible (although we don't know) that the blob objects have internal state about connections that is
    ///    better to share.
    /// </summary>
    private readonly ConcurrentDictionary<AbsoluteContainerPath, BlobContainerClient> _clients = new();

    public ShardedBlobCacheTopology(Configuration configuration)
    {
        _configuration = configuration;

        _blobClientOptions = new BlobClientOptions()
        {
            Retry = {
                MaxRetries = configuration.BlobRetryPolicy.MaxRetries,
                Delay = configuration.BlobRetryPolicy.RetryDelay,
                NetworkTimeout = configuration.BlobRetryPolicy.NetworkTimeout
            }
        };
        _scheme = _configuration.ShardingScheme.Create();
        _containers = GenerateContainerNames(_configuration.Universe, _configuration.Namespace, _configuration.ShardingScheme);
    }

    internal static BlobCacheContainerName[] GenerateContainerNames(string universe, string @namespace, ShardingScheme scheme)
    {
        var matrices = scheme.GenerateMatrix();
        return Enum.GetValues(typeof(BlobCacheContainerPurpose)).Cast<BlobCacheContainerPurpose>().Select(
            purpose =>
            {
                // Different matrix implies different containers, and therefore different universes.
                var matrix = purpose switch
                {
                    BlobCacheContainerPurpose.Content => matrices.Content,
                    BlobCacheContainerPurpose.Metadata => matrices.Metadata,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(purpose),
                        purpose,
                        $"Unknown value for {nameof(BlobCacheContainerPurpose)}: {purpose}"),
                };

                return new BlobCacheContainerName(
                    BlobCacheVersion.V0,
                    purpose,
                    matrix,
                    universe,
                    @namespace);
            }).ToArray();
    }

    public async Task<(BlobContainerClient Client, AbsoluteContainerPath Path)> GetContainerClientAsync(OperationContext context, BlobCacheShardingKey key)
    {
        var account = _scheme.Locate(key.Key);
        Contract.Assert(account is not null, $"Attempt to determine account for key `{key}` failed");

        // _containers is created with this same enum, so this index access is safe.
        var container = _containers[(int)key.Purpose];

        var path = new AbsoluteContainerPath(account, container);

        return (await GetOrCreateClientAsync(context, path), path);
    }

    private async Task<BlobContainerClient> GetOrCreateClientAsync(
        OperationContext context,
        AbsoluteContainerPath absoluteContainerPath)
    {
        // NOTE: We don't use AddOrGet because CreateClientAsync could fail, in which case we'd have a task that would
        // fail everyone using this.
        if (_clients.TryGetValue(absoluteContainerPath, out var client))
        {
            return client;
        }

        using var guard = await _locks.AcquireAsync(absoluteContainerPath, context.Token);
        if (_clients.TryGetValue(absoluteContainerPath, out client))
        {
            return client;
        }

        client = await CreateClientAsync(context, absoluteContainerPath.Account, absoluteContainerPath.Container).ThrowIfFailureAsync();

        var added = _clients.TryAdd(absoluteContainerPath, client);
        Contract.Assert(added, "Impossible condition happened: lost TryAdd race under a lock");

        return client;
    }

    public IEnumerable<AbsoluteContainerPath> EnumerateContainers(OperationContext context, BlobCacheContainerPurpose purpose)
    {
        var container = _containers[(int)purpose];
        foreach (var account in _configuration.ShardingScheme.Accounts)
        {
            yield return new AbsoluteContainerPath(account, container);
        }
    }

    public async IAsyncEnumerable<BlobContainerClient> EnumerateClientsAsync(
        OperationContext context,
        BlobCacheContainerPurpose purpose)
    {
        foreach (var absoluteContainerPath in EnumerateContainers(context, purpose))
        {
            var client = await GetOrCreateClientAsync(context, absoluteContainerPath);
            yield return client;
        }
    }

    public async Task<(BlobClient Client, AbsoluteBlobPath Path)> GetBlobClientAsync(OperationContext context, ContentHash contentHash)
    {
        var (container, containerPath) = await GetContainerClientAsync(context, BlobCacheShardingKey.FromContentHash(contentHash));
        var blobPath = BlobPath.CreateAbsolute($"{contentHash}.blob");
        var client = container.GetBlobClient(blobPath.Path);
        return new(client, new AbsoluteBlobPath(containerPath, blobPath));
    }

    private Task<Result<BlobContainerClient>> CreateClientAsync(OperationContext context, BlobCacheStorageAccountName account, BlobCacheContainerName container)
    {
        var msg = $"Account=[{account}] Container=[{container}]";
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var credentials = await _configuration.SecretsProvider.RetrieveBlobCredentialsAsync(context, account);
                var containerClient = credentials.CreateContainerClient(container.ContainerName, _blobClientOptions);

                try
                {
                    await containerClient.CreateIfNotExistsAsync(
                        Azure.Storage.Blobs.Models.PublicAccessType.None,
                        null,
                        null,
                        cancellationToken: context.Token);
                }
                catch (RequestFailedException exception)
                {
                    throw new InvalidOperationException(message: $"Container `{container}` does not exist in account `{account}` and could not be created", innerException: exception);
                }

                return Result.Success(containerClient);
            },
            extraStartMessage: msg,
            extraEndMessage: _ => msg,
            timeout: _configuration.ClientCreationTimeout ?? Timeout.InfiniteTimeSpan);
    }
}
