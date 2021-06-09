// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class RocksDbContentMetadataStore : StartupShutdownSlimBase, IContentMetadataStore
    {
        public RocksDbContentMetadataDatabase Database { get; }

        public bool AreBlobsSupported => true;

        public override bool AllowMultipleStartupAndShutdowns => true;

        protected override Tracer Tracer { get; } = new Tracer(nameof(RocksDbContentMetadataStore));

        public RocksDbContentMetadataStore(
            IClock clock,
            RocksDbContentLocationDatabaseConfiguration configuration)
        {
            Database = new RocksDbContentMetadataDatabase(clock, configuration);
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
            return Task.FromResult(PutBlobResult.OutOfCapacity(hash, 0, ""));
        }

        public Task<GetBlobResult> GetBlobAsync(OperationContext context, ShortHash hash)
        {
            return Task.FromResult(new GetBlobResult(hash, null));
        }
    }
}
