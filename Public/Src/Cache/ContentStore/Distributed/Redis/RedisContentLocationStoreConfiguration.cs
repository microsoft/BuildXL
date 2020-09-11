// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Configuration properties for <see cref="RedisGlobalStore"/>
    /// </summary>
    public class RedisContentLocationStoreConfiguration : LocalLocationStoreConfiguration
    {
        /// <summary>
        /// The keyspace under which all keys in redis are stored
        /// </summary>
        public string Keyspace { get; set; }

        /// <summary>
        /// Gets or sets size of batch calls to Redis.
        /// </summary>
        public int RedisBatchPageSize { get; set; } = RedisContentLocationStoreConstants.DefaultBatchSize;

        /// <summary>
        /// The time before a machine is marked as closed from its last heartbeat as open.
        /// </summary>
        public TimeSpan MachineActiveToClosedInterval { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// The time before machines are marked as expired and locations are eligible for garbage collection from the local database
        /// </summary>
        public TimeSpan MachineActiveToExpiredInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// The interval at which cluster state should be mirror by master machine
        /// </summary>
        public TimeSpan ClusterStateMirrorInterval => MachineActiveToExpiredInterval.Multiply(0.5);

        /// <summary>
        /// Indicates whether cluster state should be mirrored to secondary redis
        /// </summary>
        public bool MirrorClusterState { get; set; } = true;

        /// <summary>
        /// Indicates whether optimistic form of RegisterLocalLocation is used which uses a single set if not exists operation
        /// to set size, local machine bit, and expiry. This falls back to multi-operation version with distinct operations for
        /// setting size, local machine bit, and expiry.
        /// </summary>
        public bool UseOptimisticRegisterLocalLocation { get; set; } = true;

        /// <summary>
        /// Default configuration instance.
        /// </summary>
        public static RedisContentLocationStoreConfiguration Default { get; } = new RedisContentLocationStoreConfiguration()
        {
            Keyspace = "Default:"
        };

        /// <summary>
        /// Expiry time for all blobs stored in Redis.
        /// </summary>
        public int BlobExpiryTimeMinutes { get; set; } = 0;

        /// <summary>
        /// Max size for storing blobs in the ContentLocationStore
        /// </summary>
        public long MaxBlobSize { get; set; } = 1024 * 4;

        /// <summary>
        /// Max capacity for blobs in the ContentLocationStore
        /// </summary>
        public long MaxBlobCapacity { get; set; } = 1024 * 1024 * 1024;

        /// <summary>
        /// Returns true if Redis can be used for storing small files.
        /// </summary>
        public bool AreBlobsSupported => BlobExpiryTimeMinutes > 0 && MaxBlobCapacity > 0 && MaxBlobSize > 0;

        /// <summary>
        /// The number of consecutive redis connection errors that will trigger reconnection.
        /// </summary>
        /// <remarks>
        /// Due to a bug in StackExchange.Redis, the client may fail to reconnect to Redis Azure Cache in some cases.
        /// This configuration allows us to recreate a connection if there is no successful calls to redis and all the calls are failing with connectivity issues.
        /// </remarks>
        public int RedisConnectionErrorLimit { get; set; } = int.MaxValue;

        /// <summary>
        /// The number of consecutive reconnection events that will trigger a service shutdown.
        /// </summary>
        public int RedisReconnectionLimitBeforeServiceRestart { get; set; } = int.MaxValue;

        /// <summary>
        /// Whether to trace failures in redis access layer.
        /// </summary>
        public bool TraceRedisFailures { get; set; } = false;

        /// <summary>
        /// Whether to trace transient failures in redis access layer.
        /// </summary>
        public bool TraceRedisTransientFailures { get; set; } = false;

        /// <summary>
        /// Gets a minimal time between reconnecting to a redis instance.
        /// </summary>
        public TimeSpan MinRedisReconnectInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Whether to cancel existing batches when a connection multiplexer used for creating it is closed.
        /// </summary>
        public bool CancelBatchWhenMultiplexerIsClosed { get; set; } = false;

        /// <summary>
        /// Whether to treat <see cref="ObjectDisposedException"/> in <see cref="RedisDatabaseAdapter"/> as a transient error and retry the operation or not.
        /// </summary>
        public bool TreatObjectDisposedExceptionAsTransient { get; set; } = false;

        /// <summary>
        /// Timeout for GetBlob/PutBlob operations.
        /// </summary>
        public TimeSpan BlobTimeout { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Timeout for getting .
        /// </summary>
        public TimeSpan ClusterRedisOperationTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }
}
