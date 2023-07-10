// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;
using static BuildXL.Cache.BlobLifetimeManager.Library.RocksDbLifetimeDatabase;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    /// <summary>
    /// This class is responsible for creating a database "from zero". This means that it will enumerate all of the contents of
    /// the blob cache and construct a database that tracks content blobs' reference coutns, as well as blob lengths.
    /// </summary>
    public class LifetimeDatabaseCreator
    {
        private static Tracer Tracer { get; } = new(nameof(LifetimeDatabaseCreator));

        private record ProcessFingerprintRequest(OperationContext Context, BlobContainerClient Container, BlobItem Blob, RocksDbLifetimeDatabase Database);

        private readonly IClock _clock;
        private readonly IBlobCacheTopology _topology;
        private readonly SerializationPool _serializationPool = new();

        public LifetimeDatabaseCreator(IClock clock, IBlobCacheTopology topology)
        {
            _clock = clock;
            _topology = topology;
        }

        public async Task<RocksDbLifetimeDatabase> CreateAsync(
            OperationContext context,
            int contentDegreeOfParallelism,
            int fingerprintDegreeOfParallelism)
        {
            Contract.Requires(contentDegreeOfParallelism > 0);
            Contract.Requires(fingerprintDegreeOfParallelism > 0);

            RocksDbLifetimeDatabase database = CreateNewDatabase();

            _ = await ProcessIndividualContentBlobsAsync(context, contentDegreeOfParallelism, database);
            await ProcessRemoteFingerprintsAsync(context, fingerprintDegreeOfParallelism, database);

            return database;
        }

        private Task<BoolResult> ProcessIndividualContentBlobsAsync(
            OperationContext context,
            int degreeOfParallelism,
            RocksDbLifetimeDatabase database)
        {
            long count = 0;

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var clientEnumerationTasks = new List<Task>();

                    var actionBlock = ActionBlockSlim.Create<(BlobItem blob, BlobContainerClient container)>(
                        degreeOfParallelism: degreeOfParallelism,
                        async blobAndContainer =>
                        {
                            var blob = blobAndContainer.blob;
                            var container = blobAndContainer.container;

                            try
                            {
                                if (!blob.Name.EndsWith(".blob"))
                                {
                                    return;
                                }

                                var lastAccessTime = GetLastAccessTime(blob);
                                var hashString = blob.Name[..^".blob".Length];

                                if (!ContentHash.TryParse(hashString, out var hash))
                                {
                                    Tracer.Warning(context, $"Failed to parse content hash from blob name {blob.Name}");
                                    return;
                                }

                                var length = await GetBlobLengthAsync(blob, container);
                                database.AddContent(hash, length).ThrowIfFailure();
                            }
                            catch (Exception ex)
                            {
                                Tracer.Error(context, ex, $"Error when processing content. Account=[{container.AccountName}], Container=[{container.Name}], Blob={blob.Name}");
                            }

                            if (Interlocked.Increment(ref count) % 1000 == 0)
                            {
                                Tracer.Info(context, $"Processed {count} content blobs so far.");
                            }
                        });

                    await foreach (var container in _topology.EnumerateClientsAsync(context, BlobCacheContainerPurpose.Content))
                    {
                        var enumerationTask = Task.Run(async () =>
                        {
                            Tracer.Debug(context, $"Starting enumeration of content blobs for account=[${container.AccountName}], container=[{container.Name}]");

                            await foreach (var blob in container.GetBlobsAsync(cancellationToken: context.Token))
                            {
                                actionBlock.Post((blob, container));
                            }
                        });

                        clientEnumerationTasks.Add(enumerationTask);
                    }

                    await TaskUtilities.SafeWhenAll(clientEnumerationTasks);
                    actionBlock.Complete();
                    await actionBlock.Completion;

                    return BoolResult.Success;
                },
                extraEndMessage: _ => $"TotalProcessed={count}");
        }

        private Task ProcessRemoteFingerprintsAsync(
            OperationContext context,
            int degreeOfParallelism,
            RocksDbLifetimeDatabase database)
        {
            long count = 0;

            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var clientEnumerationTasks = new List<Task>();

                    var actionBlock = ActionBlockSlim.Create<ProcessFingerprintRequest>(
                                degreeOfParallelism: degreeOfParallelism,
                                async request =>
                                {
                                    await DownloadAndProcessContentHashListAsync(request.Context, request.Container, request.Blob, request.Database);

                                    if (Interlocked.Increment(ref count) % 1000 == 0)
                                    {
                                        Tracer.Info(context, $"Processed {count} fingerprints so far.");
                                    }
                                });

                    await foreach (var container in _topology.EnumerateClientsAsync(context, BlobCacheContainerPurpose.Metadata))
                    {
                        var enumerationTask = Task.Run(async () =>
                        {
                            Tracer.Debug(context, $"Starting enumeration of fingerprints for account=[${container.AccountName}], container=[{container.Name}]");

                            await foreach (var blob in container.GetBlobsAsync(cancellationToken: context.Token))
                            {
                                actionBlock.Post(new ProcessFingerprintRequest(context, container, blob, database));
                            }
                        });

                        clientEnumerationTasks.Add(enumerationTask);
                    }

                    await TaskUtilities.SafeWhenAll(clientEnumerationTasks);
                    actionBlock.Complete();
                    await actionBlock.Completion;

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

        private RocksDbLifetimeDatabase CreateNewDatabase()
        {
            var temp = Path.Combine(Path.GetTempPath(), "LifetimeDatabase", Guid.NewGuid().ToString());
            var db = RocksDbLifetimeDatabase.Create(
                new RocksDbLifetimeDatabase.Configuration
                {
                    DatabasePath = temp,
                    LruEnumerationPercentileStep = 0.05,
                    LruEnumerationBatchSize = 1000,
                },
                _clock)
                .ThrowIfFailure();

            return db;
        }

        private async Task DownloadAndProcessContentHashListAsync(
            OperationContext context,
            BlobContainerClient container,
            BlobItem blob,
            RocksDbLifetimeDatabase database)
        {
            try
            {
                var blobClient = container.GetBlobClient(blob.Name);

                using var stream = new MemoryStream();
                var response = await blobClient.DownloadToAsync(stream, context.Token);

                if (response.IsError)
                {
                    Tracer.Debug(context, $"Failed to download content hash list: {response.ReasonPhrase}");
                    return;
                }

                // Reset the position of the stream so we can read from it.
                stream.Position = 0;
                var metadataEntry = _serializationPool.Deserialize(stream, ContentStore.Distributed.NuCache.MetadataEntry.Deserialize);
                var contentHashList = metadataEntry.ContentHashListWithDeterminism;
                var lastAccessTime = GetLastAccessTime(blob);

                await ProcessContentHashListAsync(context, blob, contentHashList, lastAccessTime, database);
            }
            catch (Exception ex)
            {
                Tracer.Error(context, ex, $"Error when processing fingerprint. Account=[{container.AccountName}], Container=[{container.Name}], Blob={blob.Name}");
            }
        }

        private async Task ProcessContentHashListAsync(
            OperationContext context,
            BlobItem blob,
            ContentHashListWithDeterminism contentHashList,
            DateTime lastAccessTime,
            RocksDbLifetimeDatabase database)
        {
            var hashes = new List<(ContentHash hash, long size)>();
            foreach (var contentHash in contentHashList.ContentHashList!.Hashes)
            {
                try
                {
                    var exists = database.GetContentEntry(contentHash) is not null;
                    var length = 0L;
                    if (!exists)
                    {
                        BlobClient client = await _topology.GetBlobClientAsync(context, contentHash);
                        var response = await client.GetPropertiesAsync(cancellationToken: context.Token);
                        length = response.Value.ContentLength;
                    }

                    hashes.Add((contentHash, length));
                }
                catch (Exception ex)
                {
                    Tracer.Error(context, ex, $"Error when incrementing reference count for {contentHash.ToShortString()}");
                }
            }

            database.AddContentHashList(
                new ContentHashList(
                    blob.Name,
                    lastAccessTime,
                    contentHashList.ContentHashList!.Hashes.ToArray(),
                    blob.Properties.ContentLength!.Value),
                hashes).ThrowIfFailure();
        }

        private static DateTime GetLastAccessTime(
            BlobItem blob)
        {
            var offset = blob.Properties.LastAccessedOn ?? blob.Properties.CreatedOn;
            Contract.Assert(offset is not null);

            return offset.Value.UtcDateTime;
        }
    }
}
