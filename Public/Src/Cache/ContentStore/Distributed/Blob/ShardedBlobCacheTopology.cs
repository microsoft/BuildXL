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
using Azure.Storage.Blobs.Models;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;

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
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(0.25);

        /// <summary>
        /// Maximum amount of time we're willing to wait for any operation against storage.
        /// </summary>
        public TimeSpan NetworkTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }

    public record Configuration(
        BuildCacheConfiguration? BuildCacheConfiguration,
        ShardingScheme ShardingScheme,
        IBlobCacheContainerSecretsProvider SecretsProvider,
        string Universe,
        string Namespace,
        BlobRetryPolicy BlobRetryPolicy,
        TimeSpan? ClientCreationTimeout = null)
    {
    }

    private readonly Configuration _configuration;

    private readonly BlobClientOptions _blobClientOptions;

    /// <summary>
    /// Holds pre-allocated container names to avoid allocating strings every time we want to get the container for a
    /// given key.
    /// </summary>
    private readonly IReadOnlyDictionary<BlobCacheStorageAccountName, BlobCacheContainerName[]> _containerMapping;
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
                NetworkTimeout = configuration.BlobRetryPolicy.NetworkTimeout,
            }
        };
        _scheme = _configuration.ShardingScheme.Create();

        ContainerNamingScheme namingScheme = _configuration.BuildCacheConfiguration == null
           ? new LegacyContainerNamingScheme(_configuration.ShardingScheme, _configuration.Universe, _configuration.Namespace)
           : new BuildCacheContainerNamingScheme(_configuration.BuildCacheConfiguration);

        _containerMapping = namingScheme.GenerateContainerNameMapping();
    }

    public async Task<(BlobContainerClient Client, AbsoluteContainerPath Path)> GetShardContainerClientWithPathAsync(OperationContext context, BlobCacheShardingKey key)
    {
        var account = _scheme.Locate(key.Key);
        Contract.Assert(account is not null, $"Attempt to determine account for key `{key}` failed");

        // _containers is created with this same enum, so this index access is safe.
        var container = _containerMapping[account][(int)key.Purpose];

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
        foreach (var account in _configuration.ShardingScheme.Accounts)
        {
            var container = _containerMapping[account][(int)purpose];
            yield return new AbsoluteContainerPath(account, container);
        }
    }

    
    public async IAsyncEnumerable<(BlobContainerClient Client, AbsoluteContainerPath Path)> EnumerateClientsAsync(
        OperationContext context,
        BlobCacheContainerPurpose purpose)
    {
        foreach (var absoluteContainerPath in EnumerateContainers(context, purpose))
        {
            var client = await GetOrCreateClientAsync(context, absoluteContainerPath);
            yield return (Client: client, Path: absoluteContainerPath);
        }
    }

    public Task<BoolResult> EnsureContainersExistAsync(OperationContext context)
    {
        return context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                // We process the creation of containers in parallel because there can be quite a few of them. It's
                // 2*numShards (so 20 in most cases, but can be much higher).

                await ParallelAlgorithms.WhenDoneAsync(
                    items: EnumerateContainers(context, BlobCacheContainerPurpose.Content).Concat(EnumerateContainers(context, BlobCacheContainerPurpose.Metadata)),
                    degreeOfParallelism: Environment.ProcessorCount,
                    cancellationToken: context.Token,
                    action: async (scheduleItem, containerPath) =>
                    {
                        var containerClient = await GetOrCreateClientAsync(context, containerPath);
                        await StorageClientExtensions.EnsureContainerExistsAsync(Tracer, context, containerClient, _configuration.ClientCreationTimeout ?? Timeout.InfiniteTimeSpan).ThrowIfFailureAsync();
                    });

                return BoolResult.Success;
            });
    }

    private Task<Result<BlobContainerClient>> CreateClientAsync(OperationContext context, BlobCacheStorageAccountName account, BlobCacheContainerName container)
    {
        var msg = $"Account=[{account}] Container=[{container}]";
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var credentials = await _configuration.SecretsProvider.RetrieveContainerCredentialsAsync(context, account, container);
                var containerClient = credentials.CreateContainerClient(container.ContainerName, _blobClientOptions);

                return Result.Success(containerClient);
            },
            extraStartMessage: msg,
            extraEndMessage: _ => msg,
            timeout: _configuration.ClientCreationTimeout ?? Timeout.InfiniteTimeSpan);
    }
}
