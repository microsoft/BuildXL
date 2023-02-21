// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Tasks;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <nodoc />
    public record BlobMetadataStoreConfiguration()
        : BlobFolderStorageConfiguration(ContainerName: "default", FolderName: "metadata/default")
    {
    }

    /// <nodoc />
    public class AzureBlobStorageMetadataStore : StartupShutdownComponentBase, IMetadataStoreWithIncorporation
    {
        /// <nodoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageMetadataStore));

        private const string SelectorPattern = @"chl.(?<hash>[^_]+)_(?<output>\w+)\.blob";
        private readonly BlobMetadataStoreConfiguration _configuration;

        private readonly BlobFolderStorage _storage;

        private readonly Regex _regex = new Regex(SelectorPattern);

        /// <nodoc />
        public AzureBlobStorageMetadataStore(
            BlobMetadataStoreConfiguration configuration)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            _configuration = configuration;

            _storage = new BlobFolderStorage(Tracer, configuration);

            LinkLifetime(_storage);
        }

        /// <nodoc />
        public Task<Result<bool>> CompareExchangeAsync(OperationContext context, StrongFingerprint strongFingerprint, SerializedMetadataEntry replacement, string expectedReplacementToken)
        {
            return _storage.CompareUpdateContentAsync(
                context,
                GetName(strongFingerprint),
                () =>
                {
                    return new MemoryStream(replacement.Data);
                },
                etag: expectedReplacementToken,
                attempt: 0);
        }

        /// <nodoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var selectors = new List<Selector>();
                    var blobs = await _storage.ListLruOrderedBlobsAsync(context, subDirectoryPath: GetWeakFingerprintPath(weakFingerprint), maxResults: 100);
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
                    var name = GetName(strongFingerprint);
                    var state = await _storage.ReadStateAsync(context, name, stream =>
                    {
                        return new ValueTask<byte[]>(stream.ToArray());
                    }).ThrowIfFailureAsync();

                    return Result.Success(new SerializedMetadataEntry()
                    {
                        ReplacementToken = state.ETag,
                        Data = state.Value
                    });
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
                        .Select(async strongFingerprintTask =>
                        {
                            var strongFingerprint = await strongFingerprintTask;
                            return await _storage.TouchAsync(context, GetName(strongFingerprint));
                        });

                    return (await TaskUtilities.SafeWhenAll(tasks)).And();
                });
        }

        private Selector ParseSelector(BlobPath name)
        {
            try
            {
                var match = _regex.Match(name.Path);
                var hashString = match.Groups["hash"].Value;
                var outputString = match.Groups["output"].Value;

                return new Selector(new ContentHash(hashString), HexUtilities.HexToBytes(outputString));
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed parsing '{name}' with '{SelectorPattern}': {ex.ToString()}");
            }
        }

        private string AsBlobFileName(Selector selector)
        {
            return $"chl.{selector.ContentHash.Serialize()}_{HexUtilities.BytesToHex(selector.Output)}.blob";
        }

        private BlobPath GetWeakFingerprintPath(Fingerprint weakFingerprint)
        {
            var fingerprintString = weakFingerprint.Serialize();
            return new BlobPath($"{fingerprintString.Substring(0, 3)}/{fingerprintString}", relative: true);
        }

        private BlobPath GetName(StrongFingerprint strongFingerprint)
        {
            return new BlobPath($"{GetWeakFingerprintPath(strongFingerprint.WeakFingerprint)}/{AsBlobFileName(strongFingerprint.Selector)}", relative: true);
        }
    }
}
