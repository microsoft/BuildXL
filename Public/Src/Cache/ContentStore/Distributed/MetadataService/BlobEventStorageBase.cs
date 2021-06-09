// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using Microsoft.WindowsAzure.Storage.Blob;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Configuration for blob based content metadata event storage
    /// </summary>
    public record BlobEventStorageConfiguration
    {
        public AzureBlobStorageCredentials Credentials { get; init; }

        public string FolderName { get; init; } = "eventlogs";

        public string ContainerName { get; init; } = "persistenteventstorage";
    }

    /// <summary>
    /// Base class for blob based content metadata event storage
    /// </summary>
    /// <typeparam name="TKey">the of the key used for computing blob name</typeparam>
    /// <typeparam name="TData">the type of the data format (some representation of a sequence of bytes)</typeparam>
    public abstract class BlobEventStorageBase<TKey, TData> : StartupShutdownSlimBase
        where TKey : IComparable<TKey>
    {
        private readonly VolatileSet<string> _createdBlobs = new VolatileSet<string>(SystemClock.Instance);

        private const long BlockSizeLimit = 4_000_000;

        private readonly BlobEventStorageConfiguration _configuration;
        private readonly CloudBlobClient _client;
        private readonly CloudBlobContainer _container;

        /// <summary>
        /// The directory used to store blobs for the event storage
        /// </summary>
        protected CloudBlobDirectory BlobDirectory { get; }

        public BlobEventStorageBase(BlobEventStorageConfiguration configuration)
        {
            _configuration = configuration;

            _client = _configuration.Credentials.CreateCloudBlobClient();
            // TODO: perhaps overwrite retry policy?
            _container = _client.GetContainerReference(_configuration.ContainerName);
            BlobDirectory = _container.GetDirectoryReference(_configuration.FolderName);
        }

        /// <summary>
        /// Converts the data format to a stream
        /// </summary>
        protected abstract Stream AsStream(TData data);

        /// <summary>
        /// Converts a memory stream to the data format
        /// </summary>
        protected abstract TData FromStream(MemoryStream stream);

        /// <summary>
        /// Parses the key from the blob name
        /// </summary>
        protected abstract bool TryParseName(string blobName, out TKey key);

        /// <summary>
        /// Gets the append blob name from the key
        /// </summary>
        protected abstract string ToAppendBlobName(TKey cursor);

        /// <summary>
        /// Gets the key from a <see cref="BlockReference"/>
        /// </summary>
        protected abstract TKey ToKey(BlockReference blockReference);

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (await _container.CreateIfNotExistsAsync())
            {
                Tracer.Info(context, $"Created new blob container `{_configuration.ContainerName}`");
            }

            return BoolResult.Success;
        }

        /// <summary>
        /// Appends the blob to the blob specified by the block reference
        /// </summary>
        public Task<BoolResult> AppendAsync(OperationContext context, BlockReference cursor, TData data)
        {
            var stream = AsStream(data);
            var msg = $"{cursor} Length=[{stream.Length}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var name = ToAppendBlobName(ToKey(cursor));
                var blobReference = BlobDirectory.GetAppendBlobReference(name);
                bool createNew = _createdBlobs.Add(name, TimeSpan.FromMinutes(5));

                if (!createNew && stream.Length > 0 && stream.Length <= BlockSizeLimit)
                {
                    await blobReference.AppendBlockAsync(stream);
                }
                else
                {
                    using (var azureStream = await blobReference.OpenWriteAsync(createNew))
                    {
                        await stream.CopyToAsync(azureStream);

                        await stream.FlushAsync();
                    }
                }

                return BoolResult.Success;
            },
            traceOperationStarted: false,
            extraStartMessage: msg,
            extraEndMessage: _ => msg);
        }

        /// <summary>
        /// Garbage collects all blobs in the storage specified up to the latest represented by the key
        /// </summary>
        public Task<BoolResult> GarbageCollectAsync(OperationContext context, TKey acknowledgedLogId)
        {
            var msg = $"LogId=[{acknowledgedLogId}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                await foreach (var cloudBlob in listAsync(BlobDirectory))
                {
                    if (!TryParseName(cloudBlob.Name, out var cursor))
                    {
                        Tracer.Error(context, $"Failed to parse string `{cloudBlob.Name}` into name");
                        continue;
                    }

                    if (acknowledgedLogId.IsGreaterThan(cursor))
                    {
                        await cloudBlob.DeleteAsync();
                    }
                }

                return BoolResult.Success;
            },
            traceOperationStarted: false,
            extraStartMessage: msg,
            extraEndMessage: _ => msg);

            async IAsyncEnumerable<CloudBlob> listAsync(CloudBlobDirectory container)
            {
                BlobContinuationToken continuation = null;
                while (!context.Token.IsCancellationRequested)
                {
                    var blobs = await container.ListBlobsSegmentedAsync(
                        useFlatBlobListing: true,
                        blobListingDetails: BlobListingDetails.Metadata,
                        maxResults: null,
                        currentToken: continuation,
                        options: null,
                        operationContext: null);
                    continuation = blobs.ContinuationToken;

                    foreach (var cloudBlob in blobs.Results.OfType<CloudBlob>())
                    {
                        yield return cloudBlob;
                    }

                    if (continuation == null)
                    {
                        break;
                    }
                }

                yield break;
            }
        }

        /// <summary>
        /// Reads the blob specified by the key
        /// </summary>
        public Task<Result<Optional<TData>>> ReadAsync(OperationContext context, TKey cursor)
        {
            var msg = $"{cursor}";
            long length = -1;
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var appendBlob = BlobDirectory.GetAppendBlobReference(ToAppendBlobName(cursor));
                if (!await appendBlob.ExistsAsync())
                {
                    return Result.Success(Optional<TData>.Empty);
                }

                await appendBlob.FetchAttributesAsync();
                length = appendBlob.Properties.Length;
                var memoryStream = new MemoryStream((int)length);
                await appendBlob.DownloadToStreamAsync(memoryStream);
                memoryStream.Position = 0;
                return Result.Success(new Optional<TData> (FromStream(memoryStream)));
            },
            traceOperationStarted: false,
            extraStartMessage: msg,
            extraEndMessage: r => $"{msg} Length=[{length}]");
        }

        protected string Pad(long value)
        {
            return value.ToString().PadLeft(5, '0');
        }
    }
}
