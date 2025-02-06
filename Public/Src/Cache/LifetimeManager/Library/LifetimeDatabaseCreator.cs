// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    /// <summary>
    /// This class is responsible for creating a database "from zero". This means that it will enumerate all of the contents of
    /// the blob cache and construct a database that tracks content blobs' reference coutns, as well as blob lengths.
    /// </summary>
    public static class LifetimeDatabaseCreator
    {
        private static Tracer Tracer { get; } = new(nameof(LifetimeDatabaseCreator));

        internal record ProcessFingerprintRequest(
            OperationContext Context,
            BlobContainerClient Container,
            string BlobName,
            long BlobLength,
            RocksDbLifetimeDatabase.IAccessor Database,
            IBlobCacheTopology Topology);

        private static readonly SerializationPool SerializationPool = new();

        public static Task<Result<RocksDbLifetimeDatabase>> CreateAsync(
            OperationContext context,
            RocksDbLifetimeDatabase.Configuration configuration,
            IClock clock,
            int contentDegreeOfParallelism,
            int fingerprintDegreeOfParallelism,
            Func<BlobNamespaceId, IBlobCacheTopology> topologyFactory)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    Contract.Requires(contentDegreeOfParallelism > 0);
                    Contract.Requires(fingerprintDegreeOfParallelism > 0);

                    RocksDbLifetimeDatabase database = CreateNewDatabase(configuration, clock);

                    // TODO: consider doing this in parallel, although it could be argued that if each of these calls
                    // is making full use of resources, it can be better to just do this sequentially.
                    foreach (var namespaceId in configuration.BlobNamespaceIds)
                    {
                        var accessor = database.GetAccessor(namespaceId);
                        var topology = topologyFactory(namespaceId);

                        _ = await ProcessIndividualContentBlobsAsync(context, contentDegreeOfParallelism, accessor, topology);
                        var fingerprintsResult = await ProcessRemoteFingerprintsAsync(context, fingerprintDegreeOfParallelism, accessor, topology, clock);

                        if (!fingerprintsResult.Succeeded)
                        {
                            // If we fail to process remote fingerprints, we fall into an unrecoverable state. We must fail.
                            return new Result<RocksDbLifetimeDatabase>(fingerprintsResult);
                        }
                    }

                    return database;
                });
        }

        private static Task<BoolResult> ProcessIndividualContentBlobsAsync(
            OperationContext context,
            int degreeOfParallelism,
            RocksDbLifetimeDatabase.IAccessor database,
            IBlobCacheTopology topology)
        {
            long count = 0;

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var processContentActionBlock = ActionBlockSlim.Create<(BlobItem blob, BlobContainerClient container)>(
                        degreeOfParallelism: degreeOfParallelism,
                        async blobAndContainer =>
                        {
                            var blob = blobAndContainer.blob;
                            var container = blobAndContainer.container;

                            await ProcessContentBlobAsync(context, blob, container, database);

                            if (Interlocked.Increment(ref count) % 1000 == 0)
                            {
                                Tracer.Info(context, $"Processed {count} content blobs so far.");
                            }
                        });

                    var clientEnumerationTasks = new List<Task>();
                    await foreach (var container in topology.EnumerateClientsAsync(context, BlobCacheContainerPurpose.Content))
                    {
                        var enumerationTask = Task.Run(async () =>
                        {
                            Tracer.Debug(context, $"Starting enumeration of content blobs for account=[${container.AccountName}], container=[{container.Name}]");

                            await foreach (var blob in container.GetBlobsAsync(cancellationToken: context.Token))
                            {
                                processContentActionBlock.Post((blob, container));
                            }
                        });

                        clientEnumerationTasks.Add(enumerationTask);
                    }

                    await TaskUtilities.SafeWhenAll(clientEnumerationTasks);
                    processContentActionBlock.Complete();
                    await processContentActionBlock.Completion;

                    return BoolResult.Success;
                },
                extraEndMessage: _ => $"TotalProcessed={count}");
        }

        private static async Task ProcessContentBlobAsync(
            OperationContext context,
            BlobItem blob,
            BlobContainerClient container,
            RocksDbLifetimeDatabase.IAccessor database)
        {
            try
            {
                if (!BlobUtilities.TryExtractContentHashFromBlobName(blob.Name, out var hashString))
                {
                    return;
                }

                if (!ContentHash.TryParse(hashString, out var hash))
                {
                    Tracer.Warning(context, $"Failed to parse content hash from blob name {blob.Name}");
                    return;
                }

                var length = await GetBlobLengthAsync(blob, container);
                database.AddContent(hash, length);
            }
            catch (Exception ex)
            {
                Tracer.Error(context, ex, $"Error when processing content. Account=[{container.AccountName}], Container=[{container.Name}], Blob={blob.Name}");
            }
        }

        private static Task<BoolResult> ProcessRemoteFingerprintsAsync(
            OperationContext context,
            int degreeOfParallelism,
            RocksDbLifetimeDatabase.IAccessor database,
            IBlobCacheTopology topology,
            IClock clock)
        {
            long count = 0;

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    BoolResult errorResult = BoolResult.Success;
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
                    var processFingerprintActionBlock = ActionBlockSlim.Create<ProcessFingerprintRequest>(
                                degreeOfParallelism: degreeOfParallelism,
                                async request =>
                                {
                                    var processResult = await DownloadAndProcessContentHashListAsync(request.Context, request.Container, request.BlobName, request.BlobLength, request.Database, request.Topology, clock);
                                    if (!processResult.Succeeded)
                                    {
                                        errorResult &= new BoolResult(processResult);
                                        cts.Cancel();
                                    }

                                    Interlocked.Increment(ref count);
                                },
                                cancellationToken: cts.Token);

                    var clientEnumerationTasks = new List<Task>();
                    await foreach (var container in topology.EnumerateClientsAsync(context, BlobCacheContainerPurpose.Metadata))
                    {
                        var enumerationTask = Task.Run(async () =>
                        {
                            Tracer.Debug(context, $"Starting enumeration of fingerprints for account=[${container.AccountName}], container=[{container.Name}]");

                            await foreach (var blob in container.GetBlobsAsync(cancellationToken: context.Token))
                            {
                                if (cts.IsCancellationRequested)
                                {
                                    break;
                                }

                                processFingerprintActionBlock.Post(new ProcessFingerprintRequest(context, container, blob.Name, blob.Properties.ContentLength!.Value, database, topology));
                            }
                        });

                        clientEnumerationTasks.Add(enumerationTask);
                    }

                    await TaskUtilities.SafeWhenAll(clientEnumerationTasks);
                    processFingerprintActionBlock.Complete();
                    await processFingerprintActionBlock.Completion;

                    if (cts.Token.IsCancellationRequested)
                    {
                        if (context.Token.IsCancellationRequested)
                        {
                            return new BoolResult("Operation was cancelled");
                        }
                        else
                        {
                            // Cancellation was requested because we failed to process one or more content hash list.
                            return errorResult;
                        }
                    }

                    return BoolResult.Success;
                },
                extraEndMessage: _ => $"TotalProcessed={count}");
        }

        private static async ValueTask<long> GetBlobLengthAsync(BlobItem blob, BlobContainerClient client)
        {
            // In practice this has always come back as not null, but just to be safe, adding a backup call.
            if (blob.Properties.ContentLength is not null)
            {
                return blob.Properties.ContentLength.Value;
            }

            var blobClient = client.GetBlobClient(blob.Name);
            var properties = await blobClient.GetPropertiesAsync();
            return properties.Value.ContentLength;
        }

        private static RocksDbLifetimeDatabase CreateNewDatabase(
            RocksDbLifetimeDatabase.Configuration configuration,
            IClock clock)
        {
            var db = RocksDbLifetimeDatabase.Create(configuration, clock);

            db.SetCreationTime(clock.UtcNow);

            return db;
        }

        internal enum ProcessContentHashListResult
        {
            Success,
            ContentHashListDoesNotExist,
            InvalidAndDeleted,
        }

        internal static Task<Result<ProcessContentHashListResult>> DownloadAndProcessContentHashListAsync(
            OperationContext context,
            BlobContainerClient container,
            string blobName,
            long blobLength,
            RocksDbLifetimeDatabase.IAccessor database,
            IBlobCacheTopology topology,
            IClock clock)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var blobClient = container.GetBlobClient(blobName);

                    // TODO: consider using pooled memory streams.
                    using var stream = new MemoryStream();

                    BlobQuotaKeeper.BlobVersion? version = null;
                    try
                    {
                        var response = await blobClient.DownloadToAsync(stream, context.Token);

                        if (response.IsError)
                        {
                            return new Result<ProcessContentHashListResult>($"Download of the content hash list failed with status code: {response.Status}");
                        }

                        version = BlobQuotaKeeper.BlobVersion.FromBlobResponse(response);
                    }
                    catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobNotFound)
                    {
                        // The CHL no longer exists. We can continue without making any changes.
                        return ProcessContentHashListResult.ContentHashListDoesNotExist;
                    }

                    // Reset the position of the stream so we can read from it.
                    stream.Position = 0;
                    var metadataEntry = SerializationPool.Deserialize(stream, MetadataEntry.Deserialize);
                    var contentHashList = metadataEntry.ContentHashListWithDeterminism;

                    // We use "now" as last access time because we just downloaded the blob, and thus we just affected the CHL's blob last access time.
                    // Adding a minute to account for clocks not being in sync. In the grand scheme of things, an error of one minute shouldn't make a difference.
                    var lastAccessTime = clock.UtcNow.AddMinutes(1);
                    if (version is not null)
                    {
                        version = version.Value with { LastAccessTimeUtc = version.Value.LastAccessTimeUtc.Max(lastAccessTime) };
                    }
                    else
                    {
                        version = BlobQuotaKeeper.BlobVersion.FromLastAccessTime(lastAccessTime);
                    }

                    var processResult = await ProcessContentHashListAsync(context, blobName, blobLength, contentHashList, lastAccessTime, database, topology);

                    if (!processResult.Succeeded)
                    {
                        return new Result<ProcessContentHashListResult>(processResult);
                    }

                    if (!processResult.Value)
                    {
                        // This signals that one of the referenced blobs does not exist. The CHL is invalid and should be deleted.
                        var deleteResult = await BlobQuotaKeeper.DeleteBlobFromStorageAsync(context, blobClient, blobLength, version);
                        if (!deleteResult.Succeeded)
                        {
                            return new Result<ProcessContentHashListResult>(deleteResult, "Failed to delete invalid content hash list.");
                        }

                        return ProcessContentHashListResult.InvalidAndDeleted;
                    }

                    return ProcessContentHashListResult.Success;
                },
                traceOperationStarted: false,
                extraEndMessage: result => $"ReturnCode=[{(result.Succeeded ? result.Value : "Failure")}], Account=[{container.AccountName}], Container=[{container.Name}], Blob=[{blobName}]");
        }

        private static async Task<Result<bool>> ProcessContentHashListAsync(
            OperationContext context,
            string blobName,
            long blobLength,
            ContentHashListWithDeterminism contentHashList,
            DateTime lastAccessTime,
            RocksDbLifetimeDatabase.IAccessor database,
            IBlobCacheTopology topology)
        {
            var hashes = new List<(ContentHash hash, long size)>();

            var strongFingerprint = AzureBlobStorageMetadataStore.ExtractStrongFingerprintFromPath(blobName);

            // The selector of a fingerprint is implicitly a piece of content that should be kept alive by the cache
            // as long as the content hash list exists. Treating it as if it was part of the CHL.
            var selectorHash = strongFingerprint.Selector.ContentHash;

            foreach (var contentHash in contentHashList.ContentHashList!.Hashes.Append(selectorHash))
            {
                try
                {
                    if (!contentHash.IsValid)
                    {
                        Tracer.Warning(context, $"Found invalid hash. Hash=[{contentHash.ToShortHash()}] CHL=[{blobName}]");
                        return false;
                    }

                    if (contentHash.IsEmptyHash() || contentHash.IsZero())
                    {
                        hashes.Add((contentHash, 0));
                        continue;
                    }

                    var exists = database.GetContentEntry(contentHash) is not null;
                    var length = 0L;
                    if (!exists)
                    {
                        var (client, _) = await topology.GetContentBlobClientAsync(context, contentHash);

                        try
                        {
                            var response = await client.GetPropertiesAsync(cancellationToken: context.Token);
                            length = response.Value.ContentLength;
                        }
                        catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobNotFound)
                        {
                            Tracer.Warning(context, $"Non-registrated content was not found in blob storage. Hash=[{contentHash.ToShortHash()}], CHL=[{blobName}]");
                            return false;
                        }
                    }

                    hashes.Add((contentHash, length));
                }
                catch (Exception ex)
                {
                    return new Result<bool>(ex, $"Error when incrementing reference count for {contentHash.ToShortString()}");
                }
            }

            database.AddContentHashList(
                new ContentHashList(
                    blobName,
                    lastAccessTime,
                    hashes.SelectList(hashWithSize => hashWithSize.hash).ToArray(),
                    blobLength),
                hashes);

            return true;
        }
    }
}
