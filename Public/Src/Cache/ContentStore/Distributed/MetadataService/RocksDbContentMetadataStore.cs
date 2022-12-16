// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class RocksDbContentMetadataStoreConfiguration
    {
        public RocksDbContentMetadataDatabaseConfiguration Database { get; init; }

        public bool DisableRegisterLocation { get; init; }
    }

    public class RocksDbContentMetadataStore : StartupShutdownComponentBase, IContentMetadataStore
    {
        public RocksDbContentMetadataDatabase Database { get; }

        private readonly RocksDbContentMetadataStoreConfiguration _configuration;

        public override bool AllowMultipleStartupAndShutdowns => true;

        protected override Tracer Tracer { get; } = new Tracer(nameof(RocksDbContentMetadataStore));

        public RocksDbContentMetadataStore(
            IClock clock,
            RocksDbContentMetadataStoreConfiguration configuration)
        {
            _configuration = configuration;
            Database = new RocksDbContentMetadataDatabase(clock, configuration.Database);
            LinkLifetime(Database);
        }

        public Task<Result<IReadOnlyList<ContentLocationEntry>>> GetBulkAsync(OperationContext context, IReadOnlyList<ShortHash> contentHashes)
        {
            var entries = new ContentLocationEntry[contentHashes.Count];
            for (var i = 0; i < contentHashes.Count; i++)
            {
                var hash = contentHashes[i];
                if (!Database.TryGetEntry(context, hash, out var entry))
                {
                    entry = ContentLocationEntry.Missing;
                }

                entries[i] = entry;
            }

            return Task.FromResult(Result.Success<IReadOnlyList<ContentLocationEntry>>(entries));
        }

        public ValueTask<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> contentHashes, bool touch)
        {
            if (!_configuration.DisableRegisterLocation)
            {
                Database.LocationAdded(context, machineId, contentHashes, touch);
            }

            return BoolResult.SuccessValueTask;
        }

        public ValueTask<BoolResult> DeleteLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHash> contentHashes)
        {
            Database.LocationRemoved(context, machineId, contentHashes);
            return BoolResult.SuccessValueTask;
        }

        public Task<Result<bool>> CompareExchangeAsync(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            SerializedMetadataEntry replacement,
            string expectedReplacementToken)
        {
            var result = Database.CompareExchange(
                context,
                strongFingerprint,
                replacement,
                expectedReplacementToken,
                null).ToResult();

            return Task.FromResult(result);
        }

        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            var selectors = Database.GetSelectors(context, weakFingerprint);
            return Task.FromResult(selectors.Select(s => new LevelSelectors(s, hasMore: false)));
        }

        public Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            var result = Database.GetSerializedContentHashList(context, strongFingerprint);
            return Task.FromResult(result);
        }
    }
}
