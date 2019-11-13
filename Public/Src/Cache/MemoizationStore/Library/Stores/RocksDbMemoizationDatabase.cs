// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    /// Defines a database which stores memoization information
    /// </summary>
    public class RocksDbMemoizationDatabase : MemoizationDatabase
    {
        internal RocksDbContentLocationDatabase Database { get; }

        /// <inheritdoc />
        protected override Tracer Tracer { get; }

        /// <nodoc />
        public RocksDbMemoizationDatabase(RocksDbMemoizationStoreConfiguration config, IClock clock)
            : this(new RocksDbContentLocationDatabase(clock, config.Database, () => new MachineId[] { }))
        {
        }

        /// <nodoc />
        public RocksDbMemoizationDatabase(RocksDbContentLocationDatabase database)
        {
            Tracer = new Tracer(nameof(RocksDbMemoizationDatabase));
            Database = database;
        }

        /// <inheritdoc />
        public override Task<Result<bool>> CompareExchange(OperationContext context, StrongFingerprint strongFingerprint, string replacementToken, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement)
        {
            return Task.FromResult(Database.CompareExchange(context, strongFingerprint, expected, replacement).ToResult());
        }

        /// <inheritdoc />
        public override Task<IEnumerable<StructResult<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            return Task.FromResult(Database.EnumerateStrongFingerprints(context));
        }

        /// <inheritdoc />
        public override Task<Result<(ContentHashListWithDeterminism contentHashListInfo, string replacementToken)>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            var contentHashListResult = Database.GetContentHashList(context, strongFingerprint);
            return contentHashListResult.Succeeded
                ? Task.FromResult(new Result<(ContentHashListWithDeterminism, string)>((contentHashListResult.ContentHashListWithDeterminism, string.Empty)))
                : Task.FromResult(new Result<(ContentHashListWithDeterminism, string)>(contentHashListResult));
        }

        /// <inheritdoc />
        public override Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return Task.FromResult(LevelSelectors.Single(Database.GetSelectors(context, weakFingerprint)));
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return Database.StartupAsync(context);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return Database.ShutdownAsync(context);
        }
    }
}
