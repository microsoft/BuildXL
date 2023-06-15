// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
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
    }

    /// <nodoc />
    public class AzureBlobStorageMetadataStore : StartupShutdownComponentBase, IMetadataStoreWithIncorporation, IMetadataStoreWithContentPinNotification
    {
        /// <nodoc />
        protected sealed override Tracer Tracer { get; } = new(nameof(AzureBlobStorageMetadataStore));

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

            var client = await GetStrongFingerprintClientAsync(context, strongFingerprint);
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
            return NotifyContentWasPinnedInternalAsync(context, strongFingerprint, DateTime.UtcNow);
        }

        internal Task<Result<bool>> UpdateLastContentPinnedTimeForTestingAsync(OperationContext context, StrongFingerprint strongFingerprint, DateTime testDateTime)
        {
            return NotifyContentWasPinnedInternalAsync(context, strongFingerprint, testDateTime);
        }

        private async Task<Result<bool>> NotifyContentWasPinnedInternalAsync(OperationContext context, StrongFingerprint strongFingerprint, DateTime now)
        {
            PooledObjectWrapper<Dictionary<string, string>>? metadataWrapper = null;

            metadataWrapper = _lastContentPinnedTime.GetInstance();
            Dictionary<string, string>? metadata = metadataWrapper.Value.Instance;
            metadata[LastContentPinnedTime] = now.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

            try
            {
                var client = await GetStrongFingerprintClientAsync(context, strongFingerprint);
                var result = await _storageClientAdapter.UpdateMetadataAsync(
                    context,
                    client,
                    metadata);

                return result;
            }
            finally
            {
                metadataWrapper?.Dispose();
            }
        }

        /// <nodoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var selectors = new List<Selector>();
                    var client = await GetContainerClientAsync(context, weakFingerprint);
                    var blobs = await _storageClientAdapter.ListLruOrderedBlobsAsync(
                        context,
                        client,
                        GetWeakFingerprintPrefix(weakFingerprint));
                    foreach (var blob in blobs)
                    {
                        selectors.Add(ParseSelector(blob));
                    }

                    return Result.Success(new LevelSelectors(selectors, false));
                });
        }

        /// <nodoc />
        public Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var client = await GetStrongFingerprintClientAsync(context, strongFingerprint);
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
                });
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
                                return await _storageClientAdapter.TouchAsync(context, await GetStrongFingerprintClientAsync(context, strongFingerprint));
                            });

                    return (await TaskUtilities.SafeWhenAll(tasks)).And();
                });
        }

        private string GetWeakFingerprintPrefix(Fingerprint weakFingerprint)
        {
            return weakFingerprint.Serialize();
        }

        private Task<BlobContainerClient> GetContainerClientAsync(OperationContext context, Fingerprint weakFingerprint)
        {
            return _blobCacheTopology.GetContainerClientAsync(context, BlobCacheShardingKey.FromWeakFingerprint(weakFingerprint));
        }

        private async Task<BlobClient> GetStrongFingerprintClientAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            var client = await GetContainerClientAsync(context, strongFingerprint.WeakFingerprint);

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
            return client.GetBlobClient(blobPath);
        }

        /// <summary>
        /// WARNING: MUST SYNC WITH <see cref="GetStrongFingerprintClientAsync(OperationContext, StrongFingerprint)"/>
        /// </summary>
        private readonly Regex _selectorRegex = new Regex(@"(?<weakFingerprint>[A-Z0-9]+)/(?<selectorContentHash>[^_]+)(?:_(?<selectorOutput>.*))?");

        private Selector ParseSelector(BlobPath name)
        {
            try
            {
                var match = _selectorRegex.Match(name.Path);

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
}
