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
        public long MaxBlobCapacity { get; init; } = 100_000;

        public RocksDbContentLocationDatabaseConfiguration Database { get; init; }
    }

    public class RocksDbContentMetadataStore : StartupShutdownComponentBase, IContentMetadataStore
    {
        public RocksDbContentMetadataDatabase Database { get; }

        private readonly RocksDbContentMetadataStoreConfiguration _configuration;

        public bool AreBlobsSupported => true;

        public override bool AllowMultipleStartupAndShutdowns => true;

        protected override Tracer Tracer { get; } = new Tracer(nameof(RocksDbContentMetadataStore));

        private DatabaseCapacity _capacity;

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
            foreach (var hash in contentHashes.AsStructEnumerable())
            {
                Database.LocationAdded(context, hash.Hash, machineId, hash.Size, updateLastAccessTime: touch);
            }

            return BoolResult.SuccessValueTask;
        }

        public Task<PutBlobResult> PutBlobAsync(OperationContext context, ShortHash hash, byte[] blob)
        {

            var capacity = _capacity;
            if (capacity?.Group != Database.ActiveColumnsGroup)
            {
                Interlocked.CompareExchange(ref _capacity, new DatabaseCapacity()
                {
                    Group = Database.ActiveColumnsGroup,
                    Remaining = _configuration.MaxBlobCapacity,
                },
                capacity);
            }

            if (_capacity.Remaining < blob.Length)
            {
                return Task.FromResult(PutBlobResult.OutOfCapacity(hash, blob.Length, ""));
            }

            if (Database.PutBlob(hash, blob))
            {
                Interlocked.Add(ref _capacity.Remaining, -blob.Length);
                return Task.FromResult(PutBlobResult.NewRedisEntry(hash, blob.Length, "", Math.Max(_capacity.Remaining, 0)));
            }
            else
            {
                return Task.FromResult(PutBlobResult.RedisHasAlready(hash, blob.Length, ""));
            }
        }

        public Task<GetBlobResult> GetBlobAsync(OperationContext context, ShortHash hash)
        {
            Database.TryGetBlob(hash, out var blob);
            return Task.FromResult(new GetBlobResult(hash, blob));
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

        private class DatabaseCapacity
        {
            public RocksDbContentMetadataDatabase.ColumnGroup Group { get; init; }
            public long Remaining;
        }
    }
}
