// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(RocksDbMemoizationDatabase));

        internal ContentLocationDatabase Database { get; }

        private readonly bool _ownsDatabase;

        /// <nodoc />
        public RocksDbMemoizationDatabase(RocksDbMemoizationStoreConfiguration config, IClock clock)
            : this(new RocksDbContentLocationDatabase(clock, config.Database, () => new MachineId[] { }))
        {
        }

        /// <nodoc />
        public RocksDbMemoizationDatabase(ContentLocationDatabase database, bool ownsDatabase = true)
        {
            _ownsDatabase = ownsDatabase;
            Database = database;
        }

        /// <inheritdoc />
        protected override Task<Result<bool>> CompareExchangeCore(OperationContext context, StrongFingerprint strongFingerprint, string replacementToken, ContentHashListWithDeterminism expected, ContentHashListWithDeterminism replacement)
        {
            return Task.FromResult(Database.CompareExchange(context, strongFingerprint, expected, replacement).ToResult());
        }

        /// <inheritdoc />
        public override Task<IEnumerable<StructResult<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            return Task.FromResult(Database.EnumerateStrongFingerprints(context));
        }

        /// <inheritdoc />
        protected override Task<ContentHashListResult> GetContentHashListCoreAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared)
        {
            var contentHashListResult = Database.GetContentHashList(context, strongFingerprint);
            return contentHashListResult.Succeeded
                ? Task.FromResult(new ContentHashListResult(contentHashListResult.ContentHashListWithDeterminism, string.Empty))
                : Task.FromResult(new ContentHashListResult(contentHashListResult));
        }

        /// <inheritdoc />
        protected override Task<Result<LevelSelectors>> GetLevelSelectorsCoreAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return Task.FromResult(LevelSelectors.Single(Database.GetSelectors(context, weakFingerprint)));
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (!_ownsDatabase)
            {
                return BoolResult.Success;
            }

            var result = await Database.StartupAsync(context);
            if (!result)
            {
                return result;
            }

            await Database.SetDatabaseModeAsync(isDatabaseWriteable: true);
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (!_ownsDatabase)
            {
                return BoolResult.SuccessTask;
            }

            return Database.ShutdownAsync(context);
        }
    }
}
