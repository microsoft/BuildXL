// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class BlobMetadataStoreConfiguration : IBlobFolderStorageConfiguration
    {
        public AzureBlobStorageCredentials? Credentials { get; set; }

        public string ContainerName { get; set; } = "metadatastore";

        public string FolderName { get; set; } = "memoization";

        /// <summary>
        /// WARNING: must be longer than the heartbeat interval
        /// </summary>
        public TimeSpan LeaseExpiryTime { get; set; } = TimeSpan.FromMinutes(5);

        public TimeSpan StorageInteractionTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public TimeSpan SlotWaitTime { get; set; } = TimeSpan.FromMilliseconds(1);

        public int MaxNumSlots { get; set; } = int.MaxValue;
    }

    public class BlobMetadataStore : StartupShutdownComponentBase, IMetadataStore
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobMetadataStore));

        private const string SelectorPattern = @"chl.(?<hash>[^_]+)_(?<output>\w+)\.blob";
        private readonly BlobMetadataStoreConfiguration _configuration;

        private readonly BlobFolderStorage _storage;

        private readonly Regex _regex = new Regex(SelectorPattern);

        public BlobMetadataStore(
            BlobMetadataStoreConfiguration configuration)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            _configuration = configuration;

            _storage = new BlobFolderStorage(Tracer, configuration);

            LinkLifetime(_storage);
        }

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

        public Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var name = GetName(strongFingerprint);
                    var state = await _storage.ReadStateAsync<byte[]>(context, name, stream =>
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

        private Selector ParseSelector(BlobName name)
        {
            try
            {
                var match = _regex.Match(name.Name);
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

        private string GetWeakFingerprintPath(Fingerprint weakFingerprint)
        {
            var fingerprintString = weakFingerprint.Serialize();
            return $"{fingerprintString.Substring(0, 3)}/{fingerprintString}";
        }

        private BlobName GetName(StrongFingerprint strongFingerprint)
        {
            return $"{GetWeakFingerprintPath(strongFingerprint.WeakFingerprint)}/{AsBlobFileName(strongFingerprint.Selector)}";
        }
    }
}
