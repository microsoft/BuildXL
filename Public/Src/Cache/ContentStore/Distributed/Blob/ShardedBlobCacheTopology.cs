// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
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

    public record Configuration(
        ShardingScheme ShardingScheme,
        IBlobCacheSecretsProvider SecretsProvider,
        string Universe,
        string Namespace,
        TimeSpan? ClientCreationTimeout = null);

    private readonly Configuration _configuration;

    /// <summary>
    /// Holds pre-allocated container names to avoid allocating strings every time we want to get the container for a
    /// given key.
    /// </summary>
    private readonly BlobCacheContainerName[] _containers;
    private readonly IShardingScheme<int, BlobCacheStorageAccountName> _scheme;

    private readonly record struct Location(BlobCacheStorageAccountName Account, BlobCacheContainerName Container);

    /// <summary>
    /// Used to implement a double-checked locking pattern at the per-container level. Essentially, we don't want to
    /// waste resources by creating clients for the same container at the same time.
    /// </summary>
    private readonly LockSet<Location> _locks = new();

    /// <summary>
    /// We cache the clients because:
    /// 1. Obtaining clients requires obtaining storage credentials, which may or may not involve RPCs.
    /// 2. Once the storage credential has been obtained, we should be fine re-using it.
    /// 3. It is possible (although we don't know) that the blob objects have internal state about connections that is
    ///    better to share.
    /// </summary>
    private readonly ConcurrentDictionary<Location, BlobContainerClient> _clients = new();

    public ShardedBlobCacheTopology(Configuration configuration)
    {
        _configuration = configuration;

        _scheme = _configuration.ShardingScheme.Create();
        _containers = GenerateContainerNames(_configuration.Universe, _configuration.Namespace, _configuration.ShardingScheme);
    }

    internal static BlobCacheContainerName[] GenerateContainerNames(string universe, string @namespace, ShardingScheme scheme)
    {
        var matrices = GenerateMatrix(scheme);
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

    internal static (string Metadata, string Content) GenerateMatrix(ShardingScheme scheme)
    {
        // The matrix here ensures that metadata does not overlap across sharding schemes. Basically, whenever we add
        // or remove shards (or change the sharding algorithm), we will get a new salt. This salt will force us to use
        // a different matrix for metadata.
        //
        // Hence, sharding changes imply no metadata hits, but they do not imply no content hits. This is on
        // purpose because metadata hits guarantee content's existence, so we can't mess around with them.

        // Generate a stable hash out of the sharding scheme.
        var algorithm = (long)scheme.Scheme;
        var locations = scheme.Accounts.Select(location => HashCodeHelper.GetOrdinalIgnoreCaseHashCode64(location.AccountName)).ToArray();

        var algorithmSalt = HashCodeHelper.Combine(HashCodeHelper.Fnv1Basis64, algorithm);
        var locationsSalt = HashCodeHelper.Combine(locations);

        var metadataSalt = Math.Abs(HashCodeHelper.Combine(algorithmSalt, locationsSalt));
        // TODO: Ideally, we'd like the following to be algorithmSalt, but that would mean that GC needs to track the
        // salts differently for content and metadata.
        var contentSalt = metadataSalt;

        return (
            Metadata: metadataSalt.ToString().Substring(0, 10),
            Content: contentSalt.ToString().Substring(0, 10));
    }

    public async Task<BlobContainerClient> GetContainerClientAsync(OperationContext context, BlobCacheShardingKey key)
    {
        var account = _scheme.Locate(key.Key);
        Contract.Assert(account is not null, $"Attempt to determine account for key `{key}` failed");

        // _containers is created with this same enum, so this index access is safe.
        var container = _containers[(int)key.Purpose];

        var location = new Location(account, container);

        // NOTE: We don't use AddOrGet because CreateClientAsync could fail, in which case we'd have a task that would
        // fail everyone using this.
        if (_clients.TryGetValue(location, out var client))
        {
            return client;
        }

        using var guard = await _locks.AcquireAsync(location, context.Token);
        if (_clients.TryGetValue(location, out client))
        {
            return client;
        }

        client = await CreateClientAsync(context, account, container).ThrowIfFailureAsync();

        var added = _clients.TryAdd(location, client);
        Contract.Assert(added, "Impossible condition happened: lost TryAdd race under a lock");

        return client;
    }

    private Task<Result<BlobContainerClient>> CreateClientAsync(OperationContext context, BlobCacheStorageAccountName account, BlobCacheContainerName container)
    {
        var msg = $"Account=[{account}] Container=[{container}]";
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var credentials = await _configuration.SecretsProvider.RetrieveBlobCredentialsAsync(context, account, container);
                var containerClient = credentials.CreateContainerClient(container.ContainerName);

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
