// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <nodoc />
    public record BlobMetadataStoreConfiguration
    {
        /// <nodoc />
        public required IBlobCacheTopology Topology { get; init; }

        /// <nodoc />
        public BlobFolderStorageConfiguration BlobFolderStorageConfiguration { get; set; } = new BlobFolderStorageConfiguration();

        /// <nodoc/>
        public required bool IsReadOnly { get; init; }
    }

    /// <nodoc />
    public class AzureBlobStorageMetadataStore : StartupShutdownComponentBase, IMetadataStoreWithIncorporation, IMetadataStoreWithContentPinNotification
    {
        /// <nodoc />
        protected sealed override Tracer Tracer => _tracer;

        private readonly AzureBlobStorageMetadataStoreTracer _tracer = new(nameof(AzureBlobStorageMetadataStore));

        private readonly BlobMetadataStoreConfiguration _configuration;

        private readonly BlobStorageClientAdapter _storageClientAdapter;
        private readonly IBlobCacheTopology _blobCacheTopology;

        // The key used in the metadata dictionary to store the last time associated content was pinned for a given content hash list
        private const string LastContentPinnedTime = "LastContentPinnedTime";

        // We pool the metadata dictionary since it is used on every upload
        private readonly ObjectPool<Dictionary<string, string>> _lastContentPinnedTime = new ObjectPool<Dictionary<string, string>>(
            () => new Dictionary<string, string>(),
            map => { map.Clear(); return map; });

        /// <nodoc />
        public AzureBlobStorageMetadataStore(
            BlobMetadataStoreConfiguration configuration)
        {
            _configuration = configuration;

            _storageClientAdapter = new BlobStorageClientAdapter(Tracer, _configuration.BlobFolderStorageConfiguration);
            _blobCacheTopology = _configuration.Topology;
        }

        /// <nodoc />
        public async Task<Result<bool>> CompareExchangeAsync(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            SerializedMetadataEntry replacement,
            string expectedReplacementToken)
        {
            DateTime now = DateTime.UtcNow;
            using var metadataWrapper = _lastContentPinnedTime.GetInstance();
            var metadata = metadataWrapper.Instance;
            metadata[LastContentPinnedTime] = now.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

            var client = await _blobCacheTopology.GetClientAsync(context, strongFingerprint);
            var result = await _storageClientAdapter.CompareUpdateContentAsync(
                context,
                client,
                () => new MemoryStream(replacement.Data),
                etag: expectedReplacementToken,
                attempt: 0,
                metadata: metadata);

            // If the replacement was actually uploaded, then the associated content is assumed to have been uploaded
            // immediately before, set the last content pin time to now
            replacement.LastContentPinnedTime = now;

            return result;
        }

        /// <summary>
        /// Updates the last content pinned time in the associated strong fingerprint so <see cref="DateTime.UtcNow"/> is associated to it
        /// </summary>
        public Task<Result<bool>> NotifyContentWasPinnedAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            // In read-only mode we cannot update the last pinned time. This can result in extra preventive pinning, but it doesn't affect correctness.
            // Let's do this outside of the tracer so we don't actually count/measure this operation in that case.
            if (_configuration.IsReadOnly)
            {
                return Task.FromResult(new Result<bool>(true));
            }

            var stopwatch = Stopwatch.StartNew();
            _tracer.ContentWasPinnedStart(context);

            try
            {
                return NotifyContentWasPinnedInternalAsync(context, strongFingerprint, DateTime.UtcNow);
            }
            finally
            {
                _tracer.ContentWasPinnedStop(context, stopwatch.Elapsed);
            }
        }

        /// <summary>
        /// Content associated to the given fingerprint was pinned, but a place operation failed to find it. It is probably too late
        /// to recover from this situation for the running build, but remove the metadata (with the last time content was pinned) so that future builds can succeed.
        /// </summary>
        public Task<Result<bool>> NotifyPinnedContentWasNotFoundAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            var stopwatch = Stopwatch.StartNew();
            _tracer.PinnedContentWasNotFoundStart(context);

            try
            {
                // If the store is readonly, we cannot actually remove the metadata.
                if (_configuration.IsReadOnly)
                {
                    return Task.FromResult(Result.Success(true));
                }

                // Clear the metadata of the given entry, so the next time it is queried, preventive pinning will happen
                return UpdateMetadataAsync(context, strongFingerprint, metadata: null);
            }
            finally
            {
                _tracer.PinnedContentWasNotFoundStop(context, stopwatch.Elapsed);
            }
        }

        internal Task<Result<bool>> UpdateLastContentPinnedTimeForTestingAsync(OperationContext context, StrongFingerprint strongFingerprint, DateTime testDateTime)
        {
            return NotifyContentWasPinnedInternalAsync(context, strongFingerprint, testDateTime);
        }

        private async Task<Result<bool>> NotifyContentWasPinnedInternalAsync(OperationContext context, StrongFingerprint strongFingerprint, DateTime now)
        {
            using PooledObjectWrapper<Dictionary<string, string>>? metadataWrapper = _lastContentPinnedTime.GetInstance();
            Dictionary<string, string>? metadata = metadataWrapper.Value.Instance;
            var lastContentPinnedTimeAsString = now.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            metadata[LastContentPinnedTime] = lastContentPinnedTimeAsString;

            var result = await UpdateMetadataAsync(context, strongFingerprint, metadata);

            if (result.Succeeded)
            {
                Tracer.Info(context, $"Content for strong fingerprint '{strongFingerprint}' was preventively pinned. Strong fingerprint last pinned time was updated to {lastContentPinnedTimeAsString}.");
            }
            else
            {
                Tracer.Info(context, $"Content for strong fingerprint '{strongFingerprint}' was preventively pinned, but strong fingerprint last pinned time couldn't be updated. Subsequent queries will attempt to preventively pin again.");
            }

            return result;
        }

        private async Task<Result<bool>> UpdateMetadataAsync(OperationContext context, StrongFingerprint strongFingerprint, Dictionary<string, string>? metadata)
        {
            var client = await _blobCacheTopology.GetClientAsync(context, strongFingerprint);
            var result = await _storageClientAdapter.UpdateMetadataAsync(
                context,
                client,
                metadata);

            return result;
        }

        /// <nodoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var selectors = new List<Selector>();
                    var client = await _blobCacheTopology.GetContainerClientAsync(context, weakFingerprint);
                    var blobs = await _storageClientAdapter.ListMruOrderedBlobsAsync(
                        context,
                        client,
                        BlobCacheTopologyExtensions.GetWeakFingerprintPrefix(weakFingerprint));
                    foreach (var blob in blobs)
                    {
                        selectors.Add(BlobCacheTopologyExtensions.ExtractSelectorFromPath(blob));
                    }

                    return Result.Success(new LevelSelectors(selectors, false));
                },
                traceOperationStarted: false);
        }

        /// <nodoc />
        public Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var client = await _blobCacheTopology.GetClientAsync(context, strongFingerprint);
                    BlobStorageClientAdapter.State<byte[]> state;

                    DateTime? lastContentPinnedTime = null;

                    state = await _storageClientAdapter.ReadStateAsync(context, client, static binaryData => new ValueTask<byte[]>(binaryData.ToArray()))
                        .ThrowIfFailureAsync();

                    if (state.Metadata?.TryGetValue(LastContentPinnedTime, out string? lastContentPinnedTimeString) == true)
                    {
                        // If the entry is there, then it should be parseable
                        lastContentPinnedTime = DateTime.Parse(lastContentPinnedTimeString!, null, System.Globalization.DateTimeStyles.RoundtripKind);
                    }

                    return Result.Success(new SerializedMetadataEntry() { ReplacementToken = state.ETag, Data = state.Value, LastContentPinnedTime = lastContentPinnedTime });
                },
                traceOperationStarted: false);
        }

        /// <nodoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(OperationContext context, IEnumerable<Task<StrongFingerprint>> strongFingerprints)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var tasks = strongFingerprints
                        .Select(
                            async strongFingerprintTask =>
                            {
                                var strongFingerprint = await strongFingerprintTask;
                                return await _storageClientAdapter.TouchAsync(context, await _blobCacheTopology.GetClientAsync(context, strongFingerprint), hard: false);
                            });

                    return (await TaskUtilities.SafeWhenAll(tasks)).And();
                },
                traceOperationStarted: false);
        }

        /// <inheritdoc/>
        public Task<GetStatsResult> GetStatsAsync(Context context) => Task.FromResult(new GetStatsResult(_tracer.GetCounters()));
    }
}
