// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// A configuration class for <see cref="RedisDatabaseAdapter"/>.
    /// </summary>
    internal class RedisDatabaseAdapterConfiguration
    {
        public string KeySpace { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.RedisConnectionErrorLimit"/>
        public int RedisConnectionErrorLimit { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.RedisReconnectionLimitBeforeServiceRestart"/>
        public int RedisReconnectionLimitBeforeServiceRestart { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.OperationTimeout"/>
        public TimeSpan OperationTimeout { get; }

        /// <nodoc />
        public string DatabaseName { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.MinRedisReconnectInterval"/>
        public TimeSpan MinReconnectInterval { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.CancelBatchWhenMultiplexerIsClosed"/>
        public bool CancelBatchWhenMultiplexerIsClosed { get; }

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.TreatObjectDisposedExceptionAsTransient"/>
        public bool TreatObjectDisposedExceptionAsTransient { get; }

        private readonly int? _retryCount;

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.RetryPolicyConfiguration"/>
        public RetryPolicyConfiguration? RetryPolicyConfiguration { get; }

        public IRetryPolicy CreateRetryPolicy(Action<Exception> onRedisException)
        {
            var policy = new RedisDatabaseAdapter.RedisRetryPolicy(onRedisException, TreatObjectDisposedExceptionAsTransient);
            if (RetryPolicyConfiguration != null)
            {
                return RetryPolicyConfiguration
                    .AsRetryPolicy(policy.IsTransient, _retryCount);
            }
            else if (_retryCount != null)
            {
                return RetryPolicyFactory.GetLinearPolicy(policy.IsTransient, _retryCount.Value);
            }
            else
            {
                return RetryPolicyFactory.GetExponentialPolicy(policy.IsTransient);
            }
        }

        public RedisDatabaseAdapterConfiguration(
            string keySpace,
            int redisConnectionErrorLimit = int.MaxValue,
            int redisReconnectionLimitBeforeServiceRestart = int.MaxValue,
            int? retryCount = null,
            string? databaseName = null,
            TimeSpan? minReconnectInterval = null,
            bool cancelBatchWhenMultiplexerIsClosed = false,
            bool treatObjectDisposedExceptionAsTransient = false,
            TimeSpan? operationTimeout = null,
            RetryPolicyConfiguration? retryPolicyConfiguration = null)
        {
            _retryCount = retryCount;
            KeySpace = keySpace;
            RedisConnectionErrorLimit = redisConnectionErrorLimit;
            RedisReconnectionLimitBeforeServiceRestart = redisReconnectionLimitBeforeServiceRestart;
            DatabaseName = databaseName ?? "Default";
            MinReconnectInterval = minReconnectInterval ?? TimeSpan.Zero;
            CancelBatchWhenMultiplexerIsClosed = cancelBatchWhenMultiplexerIsClosed;
            TreatObjectDisposedExceptionAsTransient = treatObjectDisposedExceptionAsTransient;
            OperationTimeout = operationTimeout ?? RedisContentLocationStoreConfiguration.DefaultOperationTimeout;
            RetryPolicyConfiguration = retryPolicyConfiguration;
        }
    }
}
