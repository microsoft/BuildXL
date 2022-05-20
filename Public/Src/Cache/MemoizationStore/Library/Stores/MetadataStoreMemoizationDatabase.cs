using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <nodoc />
    public class MetadataStoreMemoizationDatabase : MemoizationDatabase
    {
        private readonly SerializationPool _serializationPool = new SerializationPool();

        private readonly IMetadataStore _store;
        private readonly CentralStreamStorage? _centralStorage;
        private readonly MetadataStoreMemoizationDatabaseConfiguration _configuration;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(MetadataStoreMemoizationDatabase));

        /// <nodoc />
        public MetadataStoreMemoizationDatabase(
            IMetadataStore store,
            MetadataStoreMemoizationDatabaseConfiguration? configuration = null,
            CentralStreamStorage? centralStorage = null)
        {
            _store = store;
            _centralStorage = centralStorage;
            _configuration = configuration ?? new MetadataStoreMemoizationDatabaseConfiguration();
            LinkLifetime(store);
            LinkLifetime(centralStorage);
        }

        /// <inheritdoc />
        public override Task<IEnumerable<Result<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            throw new NotImplementedException("Enumerating all strong fingerprints is not supported");
        }

        /// <inheritdoc />
        public override Task<BoolResult> IncorporateStrongFingerprintsAsync(OperationContext context, IEnumerable<Task<StrongFingerprint>> strongFingerprints)
        {
            // Ugly hack to workaround GCS dependency on IMetadataStore
            if (_store is IMetadataStoreWithIncorporation store)
            {
                return store.IncorporateStrongFingerprintsAsync(context, strongFingerprints);
            }

            return BoolResult.SuccessTask;
        }

        /// <inheritdoc />
        protected override Task<Result<LevelSelectors>> GetLevelSelectorsCoreAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return _store.GetLevelSelectorsAsync(context, weakFingerprint, level);
        }

        /// <inheritdoc />
        protected override Task<ContentHashListResult> GetContentHashListCoreAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared)
        {
            return _store.GetContentHashListAsync(context, strongFingerprint).ThenAsync(async r =>
            {
                string? diagnostics = null;
                var data = r.Value?.Data;
                if (r.Value?.ExternalDataStorageId is string storageId)
                {
                    diagnostics = $"ExternalDataStorageId='{storageId}'. ";
                    if (_centralStorage is null)
                    {
                        diagnostics += "Data is stored externally but no storage is provided.";
                    }
                    else
                    {
                        var dataResult = await _centralStorage.ReadAsync(context, storageId, async streamWithLength =>
                        {
                            byte[] payload = new byte[(int)streamWithLength.Length];
                            var readBytes = await streamWithLength.Stream.ReadAllAsync(payload, 0, payload.Length);
                            if (readBytes < payload.Length)
                            {
                                return Result.FromErrorMessage<byte[]>($"Expected {payload.Length} but only read {readBytes}");
                            }

                            return Result.Success(payload);
                        });

                        if (dataResult.Succeeded)
                        {
                            data = dataResult.Value;
                        }
                        else
                        {
                            diagnostics += dataResult.ErrorMessage;
                        }
                    }
                }

                var result = new ContentHashListResult(
                    DeserializeContentHashListWithDeterminism(data),
                    r.Value?.ReplacementToken ?? string.Empty);

                if (diagnostics != null)
                {
                    result.SetDiagnosticsForSuccess(diagnostics);
                }

                return result;
            });
        }

        /// <inheritdoc />
        protected override async Task<Result<bool>> CompareExchangeCore(OperationContext context, StrongFingerprint strongFingerprint, string expectedReplacementToken, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement)
        {
            var entry = Serialize(replacement);
            string? diagnostics = null;
            if (_centralStorage != null && entry.Data.Length > _configuration.StorageMetadataEntrySizeThreshold)
            {
                diagnostics = $"ExternalDataStorageId='{entry.ReplacementToken}' Size='{entry.Data.Length}'";
                await _centralStorage.StoreAsync(context, entry.ReplacementToken, new MemoryStream(entry.Data, writable: false))
                    .ThrowIfFailureAsync();

                entry.Data = Array.Empty<byte>();
                entry.ExternalDataStorageId = entry.ReplacementToken;
            }

            var result = await _store.CompareExchangeAsync(context, strongFingerprint, entry, expectedReplacementToken);

            // Setting a success diagnostics only if the result is successful.
            if (result && diagnostics != null)
            {
                result.SetDiagnosticsForSuccess(diagnostics);
            }

            return result;
        }

        private SerializedMetadataEntry Serialize(ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            // We use a metadata entry instead of CHL to maintain back compat when talking to redis.
            // We can use default last access time since its not actually consumed
            var metadataEntry = new MetadataEntry(contentHashListWithDeterminism, DateTime.UtcNow);
            var data = _serializationPool.Serialize(metadataEntry, (value, writer) => value.Serialize(writer));

            return new SerializedMetadataEntry()
            {
                Data = data,
                ReplacementToken = Guid.NewGuid().ToString(),
            };
        }

        private ContentHashListWithDeterminism DeserializeContentHashListWithDeterminism(byte[]? data)
        {
            if (data == null || data.Length == 0)
            {
                return default;
            }

            return _serializationPool.Deserialize(data.AsSpan(), static r => MetadataEntry.Deserialize(r)).ContentHashListWithDeterminism;
        }
    }
}
