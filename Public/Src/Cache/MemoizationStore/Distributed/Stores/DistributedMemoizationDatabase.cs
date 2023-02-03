// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Services;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    /// Defines a database which stores memoization information
    /// </summary>
    public class DistributedMemoizationDatabase : MemoizationDatabase
    {
        private readonly MemoizationDatabase _localDatabase;
        private readonly MemoizationDatabase _sharedDatabase;
        private readonly LocalLocationStore _localLocationStore;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(DistributedMemoizationDatabase));

        /// <nodoc />
        public DistributedMemoizationDatabase(MemoizationDatabase localDatabase, MemoizationDatabase sharedDatabase, LocalLocationStore localLocationStore)
        {
            _localDatabase = localDatabase;
            _sharedDatabase = sharedDatabase;
            _localLocationStore = localLocationStore;

            LinkLifetime(_sharedDatabase);
            LinkLifetime(_localDatabase);
        }

        /// <inheritdoc />
        protected override async Task<Result<bool>> CompareExchangeCore(
            OperationContext context, 
            StrongFingerprint strongFingerprint, 
            string replacementToken, 
            ContentHashListWithDeterminism expected, 
            ContentHashListWithDeterminism replacement)
        {
            var result = await _sharedDatabase.CompareExchangeAsync(context, strongFingerprint, replacementToken, expected, replacement);
            if (!result.Succeeded || !result.Value)
            {
                return result;
            }

            // Successfully updated the entry. Notify the event store.
            await _localLocationStore.EventStore.UpdateMetadataEntryAsync(context, 
                new UpdateMetadataEntryEventData(
                    _localLocationStore.ClusterState.PrimaryMachineId, 
                    strongFingerprint, 
                    new MetadataEntry(replacement, _localLocationStore.EventStore.Clock.UtcNow))).ThrowIfFailure();
            return result;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> RegisterAssociatedContentCoreAsync(OperationContext context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashList)
        {
            var associatedHashes = strongFingerprint.Selector.ContentHash.IsZero()
                ? contentHashList.ContentHashList.Hashes
                : contentHashList.ContentHashList.Hashes.ConcatAsArray(new[] { strongFingerprint.Selector.ContentHash });

            return _localLocationStore.RegisterLocalContentAsync(context, associatedHashes, touch: true);
        }

        /// <inheritdoc />
        public override Task<IEnumerable<Result<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            return _localDatabase.EnumerateStrongFingerprintsAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<ContentHashListResult> GetContentHashListCoreAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared)
        {
            var result = await GetContentHashListMultiLevelAsync(context, strongFingerprint, preferShared);
            if (result.Succeeded && result.Value.contentHashListInfo.ContentHashList != null)
            {
                // TODO: We can represent touches to content in the system with BlobContentLocationRegistry by creating
                // a content entry for the content hash list and touching that.

                // Successfully retrieved the entry. Notify the event store of the access.
                // NOTE: Empty content hash list is used to signal that this is strictly a touch
                await _localLocationStore.EventStore.UpdateMetadataEntryAsync(context,
                    new UpdateMetadataEntryEventData(
                        _localLocationStore.ClusterState.PrimaryMachineId,
                        strongFingerprint,
                        new MetadataEntry(new ContentHashListWithDeterminism(null, CacheDeterminism.None), _localLocationStore.EventStore.Clock.UtcNow))).ThrowIfFailure();
            }

            return result;
        }

        private async Task<ContentHashListResult> GetContentHashListMultiLevelAsync(
            OperationContext context, 
            StrongFingerprint strongFingerprint, 
            bool preferShared)
        {
            var firstDatabase = preferShared ? _sharedDatabase : _localDatabase;
            var secondDatabase = preferShared ? _localDatabase : _sharedDatabase;
            var firstDatabaseSource = preferShared ? ContentHashListSource.Shared : ContentHashListSource.Local;
            var secondDatabaseSource = preferShared ? ContentHashListSource.Local : ContentHashListSource.Shared;

            var firstResult = await firstDatabase.GetContentHashListAsync(context, strongFingerprint, preferShared);
            if (!firstResult.Succeeded || firstResult.Value.contentHashListInfo.ContentHashList != null)
            {
                firstResult.Source = firstDatabaseSource;
                return firstResult;
            }

            var secondResult = await secondDatabase.GetContentHashListAsync(context, strongFingerprint, preferShared);
            secondResult.Source = secondDatabaseSource;
            return secondResult;
        }

        /// <inheritdoc />
        protected override async Task<Result<LevelSelectors>> GetLevelSelectorsCoreAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            if (level == 0)
            {
                var result = await _localDatabase.GetLevelSelectorsAsync(context, weakFingerprint, level);
                return result.Select(l => new LevelSelectors(l.Selectors, hasMore: true));
            }
            else
            {
                return await _sharedDatabase.GetLevelSelectorsAsync(context, weakFingerprint, level - 1);
            }
        }

        /// <inheritdoc />
        public override async Task<BoolResult> IncorporateStrongFingerprintsAsync(OperationContext context, IEnumerable<Task<StrongFingerprint>> strongFingerprints)
        {
            var localTask = _localDatabase.IncorporateStrongFingerprintsAsync(context, strongFingerprints);
            var remoteTask = _sharedDatabase.IncorporateStrongFingerprintsAsync(context, strongFingerprints);
            await TaskUtilities.SafeWhenAll(localTask, remoteTask);
            return (await localTask) & (await remoteTask);
        }
    }
}
