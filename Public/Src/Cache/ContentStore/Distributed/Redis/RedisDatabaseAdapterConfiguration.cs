// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Practices.TransientFaultHandling;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// A configuration class for <see cref="RedisDatabaseAdapter"/>.
    /// </summary>
    internal class RedisDatabaseAdapterConfiguration
    {
        private readonly int? _retryCount;

        public string KeySpace { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.RedisConnectionErrorLimit"/>
        public int RedisConnectionErrorLimit { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.RedisReconnectionLimitBeforeServiceRestart"/>
        public int RedisReconnectionLimitBeforeServiceRestart { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.TraceRedisFailures"/>
        public bool TraceOperationFailures { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.TraceRedisTransientFailures"/>
        public bool TraceTransientFailures { get; }

        public string DatabaseName { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.MinRedisReconnectInterval"/>
        public TimeSpan MinReconnectInterval { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.CancelBatchWhenMultiplexerIsClosed"/>
        public bool CancelBatchWhenMultiplexerIsClosed { get; }

        public RetryPolicy CreateRetryPolicy(Action<Exception> onRedidException)
        {
            if (_retryCount != null)
            {
                return new RetryPolicy(new RedisDatabaseAdapter.RedisRetryPolicy(onRedidException), _retryCount.Value);
            }
            else
            {
                return new RetryPolicy(new RedisDatabaseAdapter.RedisRetryPolicy(onRedidException), RetryStrategy.DefaultExponential);
            }
        }

        public RedisDatabaseAdapterConfiguration(
            string keySpace,
            int redisConnectionErrorLimit = int.MaxValue,
            int redisReconnectionLimitBeforeServiceRestart = int.MaxValue,
            bool traceOperationFailures = false,
            bool traceTransientFailures = false,
            int? retryCount = null,
            string? databaseName = null,
            TimeSpan? minReconnectInterval = null,
            bool cancelBatchWhenMultiplexerIsClosed = false)
        {
            _retryCount = retryCount;
            KeySpace = keySpace;
            RedisConnectionErrorLimit = redisConnectionErrorLimit;
            RedisReconnectionLimitBeforeServiceRestart = redisReconnectionLimitBeforeServiceRestart;
            TraceOperationFailures = traceOperationFailures;
            TraceTransientFailures = traceTransientFailures;
            DatabaseName = databaseName ?? "Default";
            MinReconnectInterval = minReconnectInterval ?? TimeSpan.Zero;
            CancelBatchWhenMultiplexerIsClosed = cancelBatchWhenMultiplexerIsClosed;
        }
    }
}
