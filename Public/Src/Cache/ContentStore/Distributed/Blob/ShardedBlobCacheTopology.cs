// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core.Pipeline;
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

    /// <remarks>
    /// We will reuse an HttpClient for the transport backing the blob clients. HttpClient is meant to be reused anyway
    /// (https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient?view=net-7.0#instancing)
    /// but crucially we have the need to configure the amount of open connections: when using the defaults,
    /// the number of connections is unbounded, and we have observed builds where there end up being tens of thousands
    /// of open sockets, which can (and did) hit the per-process limit of open files, crashing the engine.
    /// </summary>
    private readonly HttpClient _httpClient;

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

        _httpClient = new HttpClient(
            new HttpClientHandler()
            {
                // If left unbounded, we have observed spikes of >65k open sockets (at which point we hit
                // the OS limit of open files for the process - on Linux, where sockets count as files).
                // Running builds where we limit this value all the way down to 100 didn't see
                // any noticeable performance impact, so 30k shouldn't pose a problem.
                // The configurable limit is per-client and per-server, but because we will reuse this HttpClient
                // for all BlobClients and the 'server' (blob storage endpoint) is also always the same,
                // we are effectively limiting the number of open connections in general.
                MaxConnectionsPerServer = 30_000
            });
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

    public static (string Metadata, string Content) GenerateMatrix(ShardingScheme scheme)
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

    public Task<BlobContainerClient> GetContainerClientAsync(OperationContext context, BlobCacheShardingKey key)
    {
        var account = _scheme.Locate(key.Key);
        Contract.Assert(account is not null, $"Attempt to determine account for key `{key}` failed");

        // _containers is created with this same enum, so this index access is safe.
        var container = _containers[(int)key.Purpose];

        var location = new Location(account, container);

        return GetOrCreateClientAsync(context, location);
    }

    private async Task<BlobContainerClient> GetOrCreateClientAsync(
        OperationContext context,
        Location location)
    {
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

        client = await CreateClientAsync(context, location.Account, location.Container).ThrowIfFailureAsync();

        var added = _clients.TryAdd(location, client);
        Contract.Assert(added, "Impossible condition happened: lost TryAdd race under a lock");

        return client;
    }

    public async IAsyncEnumerable<BlobContainerClient> EnumerateClientsAsync(
        OperationContext context,
        BlobCacheContainerPurpose purpose)
    {
        var container = _containers[(int)purpose];
        foreach (var account in _configuration.ShardingScheme.Accounts)
        {
            var location = new Location(account, container);
            var client = await GetOrCreateClientAsync(context, location);
            yield return client;
        }
    }

    public async Task<BlobClient> GetBlobClientAsync(OperationContext context, ContentHash contentHash)
    {
        var container = await GetContainerClientAsync(context, BlobCacheShardingKey.FromContentHash(contentHash));
        return container.GetBlobClient($"{contentHash}.blob");
    }

    private Task<Result<BlobContainerClient>> CreateClientAsync(OperationContext context, BlobCacheStorageAccountName account, BlobCacheContainerName container)
    {
        var msg = $"Account=[{account}] Container=[{container}]";
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var credentials = await _configuration.SecretsProvider.RetrieveBlobCredentialsAsync(context, account);

                BlobClientOptions blobClientOptions = new(BlobClientOptions.ServiceVersion.V2021_02_12)
                {
                    Transport = new HttpClientTransport(_httpClient)
                };

                var containerClient = credentials.CreateContainerClient(container.ContainerName, blobClientOptions);

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
