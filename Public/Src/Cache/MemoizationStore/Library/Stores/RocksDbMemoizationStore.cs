// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Time;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    ///     An IMemoizationStore implementation using RocksDb.
    /// </summary>
    public class RocksDbMemoizationStore : DatabaseMemoizationStore
    {
        /// <nodoc />
        public RocksDbContentLocationDatabase RocksDbDatabase { get; }

        /// <nodoc />
        public RocksDbMemoizationStore(IClock clock, RocksDbMemoizationStoreConfiguration config) 
            : this(new RocksDbMemoizationDatabase(config, clock))
        {
            // Do nothing. Just delegates to other constructor to allow capturing created database
        }

        /// <nodoc />
        public RocksDbMemoizationStore(RocksDbMemoizationDatabase database)
            : base(database)
        {
            RocksDbDatabase = (RocksDbContentLocationDatabase)database.Database;
        }
    }
}
