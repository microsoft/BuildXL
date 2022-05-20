// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    ///     Marker type for children of <see cref="MemoizationStore"/>
    /// </summary>
    public abstract class MemoizationStoreConfiguration
    {
        /// <summary>
        /// Create memoization store with the current config
        /// </summary>
        public abstract IMemoizationStore CreateStore(ILogger logger, IClock clock);
    }

    /// <summary>
    ///     Grouped configuration for <see cref="RocksDbMemoizationStore"/>
    /// </summary>
    public class RocksDbMemoizationStoreConfiguration : MemoizationStoreConfiguration
    {
        /// <summary>
        /// Configuration for the internal RocksDB database
        /// </summary>
        public RocksDbContentLocationDatabaseConfiguration Database { get; set; }

        /// <inheritdoc />
        public override IMemoizationStore CreateStore(ILogger logger, IClock clock)
        {
            return new RocksDbMemoizationStore(clock, this);
        }
    }
}
