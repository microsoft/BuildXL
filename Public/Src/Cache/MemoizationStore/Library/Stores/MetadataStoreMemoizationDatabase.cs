using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
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
        protected override Tracer Tracer { get; }

        /// <summary>
        /// Reflects the name of the underlying <see cref="IMetadataStore"/>
        /// </summary>
        public override string StatsProvenance { get; }

        // Stores an association of content hash to the set of strong fingerprints.
        // Populated for pin-elided entries when RetentionPolicy is configured (to notify on content-not-found),
        // and for all cache hit entries when EnableContentRecoveryOnPlaceFailure is on (to delete the fingerprint entry).
        private readonly ConcurrentBigMap<ContentHash, CompactSet<StrongFingerprint>>? _contentToFingerprintMap;

        /// <nodoc />
        public MetadataStoreMemoizationDatabase(
            IMetadataStore store,
            MetadataStoreMemoizationDatabaseConfiguration? configuration = null,
            CentralStreamStorage? centralStorage = null)
        {
            Contract.Requires(configuration?.RetentionPolicy == null || configuration.RetentionPolicy >= TimeSpan.FromDays(1));

            Tracer = new Tracer($"{store.Name}Db");
            _store = store;
            _centralStorage = centralStorage;
            _configuration = configuration ?? new MetadataStoreMemoizationDatabaseConfiguration();
            LinkLifetime(store);
            LinkLifetime(centralStorage);

            if (configuration?.RetentionPolicy != null || _configuration.EnableContentRecoveryOnPlaceFailure)
            {
                _contentToFingerprintMap = new();
            }

            StatsProvenance = _store.GetType().Name;
        }

        /// <inheritdoc />
        public override Task<IEnumerable<Result<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            throw new NotImplementedException("Enumerating all strong fingerprints is not supported");
        }

        /// <inheritdoc />
        public override async Task<BoolResult> IncorporateStrongFingerprintsAsync(OperationContext context, IEnumerable<Task<StrongFingerprint>> strongFingerprints)
        {
            // Ugly hack to workaround GCS dependency on IMetadataStore
            if (_store is IMetadataStoreWithIncorporation store)
            {
                // As it stands, propagating errors from incorporate has little utility
                // so we ignore here.
                await store.IncorporateStrongFingerprintsAsync(context, strongFingerprints).IgnoreFailure();
            }

            return BoolResult.Success;
        }

        /// <inheritdoc/>
        public override bool AssociatedContentNeedsPinning(OperationContext context, StrongFingerprint strongFingerprint, ContentHashListResult contentHashListResult)
        {
            if (_configuration.DisablePreventivePinning)
            {
                TrackContentToFingerprintIfNeeded(strongFingerprint, contentHashListResult, pinWasElided: false);
                return false;
            }

            // We didn't find a last content pinned time or the retention policy is not configured: let's pin
            if (contentHashListResult.LastContentPinnedTime == null || _configuration.RetentionPolicy == null)
            {
                Tracer.Info(context, $"Strong fingerprint '{strongFingerprint} needs preventive pinning: " +
                    $"{(contentHashListResult.LastContentPinnedTime == null ? "it does not contain a last pinned time." : "a retention policy is not configured")}");

                // Even though pin is needed, when content recovery is enabled we still track the association
                // so we can delete the fingerprint entry if content turns out to be missing or corrupt.
                TrackContentToFingerprintIfNeeded(strongFingerprint, contentHashListResult, pinWasElided: false);

                return true;
            }

            // This is how old the content hash is wrt UtcNow
            var contentHashAge = DateTime.UtcNow - contentHashListResult.LastContentPinnedTime;

            // The pin can be elided if the content's age is smaller than the configured retention time, minus a random value between 0 and 12 hours (pins are not elided
            // when they are 1 hour away from being evicted to avoid corner cases where there is an unexpectedly long time difference between content upload and metadata upload).
            // The random value is to distribute the 'expensive pin' case across multiple builds and therefore mitigate the overall impact on a single build.
            // Observe that Configuration.StorageAccountRetentionPolicy is guaranteed to be at least 1 day
            var ageLimit = _configuration.RetentionPolicy - TimeSpan.FromHours(1) - TimeSpan.FromMinutes(new Random().Next(11 * 60));

            Tracer.Info(context, $"Strong fingerprint '{strongFingerprint}' content was last pinned on ${contentHashListResult.LastContentPinnedTime}.");
            Tracer.Info(context, $"The age limit for fingerprint '{strongFingerprint}' is {ageLimit} and therefore the need for preventive pinning is: {contentHashAge >= ageLimit}");

            var result = contentHashAge >= ageLimit;

            TrackContentToFingerprintIfNeeded(strongFingerprint, contentHashListResult, pinWasElided: !result);
            
            return result;
        }

        private void TrackContentToFingerprintIfNeeded(StrongFingerprint strongFingerprint, ContentHashListResult contentHashListResult, bool pinWasElided)
        {
            (var contentHashList, _, _) = contentHashListResult;

            // When content recovery is enabled, track all cache hits so we can delete the fingerprint on failure.
            // When only retention policy is configured, track pin-elided entries so we can notify on content-not-found.
            bool shouldTrack = _contentToFingerprintMap != null
                && contentHashList.ContentHashList != null
                && (_configuration.EnableContentRecoveryOnPlaceFailure || (_configuration.RetentionPolicy != null && pinWasElided));

            if (shouldTrack)
            {
                foreach (var contentHash in contentHashList.ContentHashList!.Hashes)
                {
                    _contentToFingerprintMap!.AddOrUpdate(contentHash, strongFingerprint,
                        (contentHash, strongFingerprint) => new CompactSet<StrongFingerprint>().Add(strongFingerprint),
                        (contentHash, strongFingerprint, relatedStrongFingerprints) => relatedStrongFingerprints.Add(strongFingerprint));
                }
            }
        }

        /// <inheritdoc/>
        public override async Task<Result<bool>> AssociatedContentWasPinnedAsync(OperationContext context, StrongFingerprint strongFingerprint, ContentHashListResult contentHashListResult)
        {
            // If the store does not support pin notification, just return
            if (_store is not IMetadataStoreWithContentPinNotification storeWithPinNotification)
            {
                return new Result<bool>(true);
            }

            return await storeWithPinNotification.NotifyContentWasPinnedAsync(context, strongFingerprint);
        }

        /// <inheritdoc/>
        public override async Task ContentNotFoundOnPlaceAsync(OperationContext context, ContentHash contentHash)
        {
            if (_store is not IMetadataStoreWithContentPinNotification storeWithPinNotification)
            {
                return;
            }

            if (_contentToFingerprintMap != null && _contentToFingerprintMap.TryGet(contentHash) is var mapResult && mapResult.IsFound)
            {
                foreach (var strongFingerprint in mapResult.Item.Value)
                {
                    var notificationResult = await storeWithPinNotification.NotifyAssociatedContentWasNotFoundAsync(context, strongFingerprint);
                    if (!notificationResult.Succeeded)
                    {
                        Tracer.Warning(context, $"NotifyAssociatedContentWasNotFoundAsync failed for strong fingerprint {strongFingerprint} after content {contentHash} was not found: {notificationResult}");
                    }
                    else
                    {
                        Tracer.Info(context, $"Notified store that content {contentHash} was not found for strong fingerprint {strongFingerprint}.");
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return _store.GetStatsAsync(context);
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
                    r.Value?.ReplacementToken ?? string.Empty,
                    r.Value?.LastContentPinnedTime);

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
            // We use a metadata entry instead of CHL to maintain back compat when talking to a global store.
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
