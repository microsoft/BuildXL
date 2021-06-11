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

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class RocksDbContentMetadataStoreConfiguration
    {
        public long MaxBlobCapacity { get; init; } = 100_000;

        public RocksDbContentLocationDatabaseConfiguration Database { get; init; }
    }

    public class RocksDbContentMetadataStore : StartupShutdownSlimBase, IContentMetadataStore
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
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return Database.StartupAsync(context);
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return Database.ShutdownAsync(context);
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

        public Task<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> contentHashes, bool touch)
        {
            foreach (var hash in contentHashes)
            {
                Database.LocationAdded(context, hash.Hash, machineId, hash.Size, updateLastAccessTime: touch);
            }

            return BoolResult.SuccessTask;
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
            if (Database.TryGetBlob(hash, out var blob))
            {
                blob = null;
            }

            return Task.FromResult(new GetBlobResult(hash, blob));
        }

        private class DatabaseCapacity
        {
            public RocksDbContentMetadataDatabase.ColumnGroup Group { get; init; }
            public long Remaining;
        }
    }
}
