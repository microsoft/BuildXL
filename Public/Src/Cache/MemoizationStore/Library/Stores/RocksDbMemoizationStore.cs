// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    ///     An IMemoizationStore implementation using RocksDb.
    /// </summary>
    public class RocksDbMemoizationStore : DatabaseMemoizationStore
    {
        private RocksDbMemoizationDatabase _database;

        internal RocksDbContentLocationDatabase Database => _database.Database;

        /// <nodoc />
        public RocksDbMemoizationStore(ILogger logger, IClock clock, RocksDbMemoizationStoreConfiguration config) 
            : this(logger, new RocksDbMemoizationDatabase(config, clock))
        {
            // Do nothing. Just delegates to other constructor to allow capturing created database
        }

        /// <nodoc />
        public RocksDbMemoizationStore(ILogger logger, RocksDbMemoizationDatabase database)
            : base(logger, database)
        {
            _database = database;
        }
    }
}
