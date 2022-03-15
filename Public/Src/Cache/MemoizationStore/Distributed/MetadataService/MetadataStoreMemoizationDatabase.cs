using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <nodoc />
    public class MetadataStoreMemoizationDatabase : MemoizationDatabase
    {
        private readonly SerializationPool _serializationPool = new SerializationPool();

        private readonly IMetadataStore _store;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(MetadataStoreMemoizationDatabase));

        /// <nodoc />
        public MetadataStoreMemoizationDatabase(IMetadataStore store)
        {
            _store = store;
            LinkLifetime(store);
        }

        /// <inheritdoc />
        public override Task<IEnumerable<Result<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            throw new NotImplementedException("Enumerating all strong fingerprints is not supported");
        }

        /// <inheritdoc />
        protected override Task<Result<LevelSelectors>> GetLevelSelectorsCoreAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return _store.GetLevelSelectorsAsync(context, weakFingerprint, level);
        }

        /// <inheritdoc />
        protected override Task<ContentHashListResult> GetContentHashListCoreAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared)
        {
            return _store.GetContentHashListAsync(context, strongFingerprint).ThenAsync(r =>
            {
                return new ContentHashListResult(DeserializeContentHashListWithDeterminism(r.Value?.Data), r.Value?.ReplacementToken ?? string.Empty);
            });
        }

        /// <inheritdoc />
        protected override Task<Result<bool>> CompareExchangeCore(OperationContext context, StrongFingerprint strongFingerprint, string expectedReplacementToken, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement)
        {
            return _store.CompareExchangeAsync(context, strongFingerprint, Serialize(replacement), expectedReplacementToken);
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

        private ContentHashListWithDeterminism DeserializeContentHashListWithDeterminism(byte[] data)
        {
            if (data == null)
            {
                return default;
            }

            return _serializationPool.Deserialize(data, r => MetadataEntry.Deserialize(r)).ContentHashListWithDeterminism;
        }
    }
}
