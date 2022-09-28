// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Configuration properties for <see cref="RedisGlobalStore"/>
    /// </summary>
    public record RedisContentLocationStoreConfiguration : LocalLocationStoreConfiguration
    {
        /// <summary>
        /// The keyspace under which all keys in redis are stored
        /// </summary>
        public string Keyspace { get; set; }

        /// <summary>
        /// The time before a machine is marked as closed from its last heartbeat as open.
        /// </summary>
        public TimeSpan MachineActiveToClosedInterval { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// The time before machines are marked as expired and locations are eligible for garbage collection from the local database
        /// </summary>
        public TimeSpan MachineActiveToExpiredInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Whether to cancel existing batches when a connection multiplexer used for creating it is closed.
        /// </summary>
        public bool CancelBatchWhenMultiplexerIsClosed { get; set; } = false;

        internal IClientAccessor<MachineLocation, IGlobalCacheService> GlobalCacheClientAccessorForTests { get; set; }

        /// <nodoc />
        public MetadataStoreMemoizationDatabaseConfiguration MetadataStoreMemoization { get; set; } = new MetadataStoreMemoizationDatabaseConfiguration();
    }
}
