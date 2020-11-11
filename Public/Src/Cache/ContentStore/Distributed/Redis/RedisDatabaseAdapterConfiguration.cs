// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <nodoc />
    public class ExponentialBackoffConfiguration
    {
        /// <nodoc />
        public int RetryCount { get; }

        /// <nodoc />
        public TimeSpan MinBackoff { get; }

        /// <nodoc />
        public TimeSpan MaxBackoff { get; }

        /// <nodoc />
        public TimeSpan DeltaBackoff { get; }

        /// <nodoc />
        public ExponentialBackoffConfiguration(int retryCount, TimeSpan? minBackoff = null, TimeSpan? maxBackoff = null, TimeSpan? deltaBackoff = null)
        {
            RetryCount = retryCount;
            MinBackoff = minBackoff ?? RetryPolicyFactory.DefaultMinBackoff;
            MaxBackoff = maxBackoff ?? RetryPolicyFactory.DefaultMaxBackoff;
            DeltaBackoff = deltaBackoff ?? RetryPolicyFactory.DefaultDeltaBackoff;
        }
    }

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

        /// <inheritdoc cref="RedisContentLocationStoreConfiguration.ExponentialBackoffConfiguration"/>
        public ExponentialBackoffConfiguration? ExponentialBackoffConfiguration { get; }

        public IRetryPolicy CreateRetryPolicy(Action<Exception> onRedisException)
        {
            var policy = new RedisDatabaseAdapter.RedisRetryPolicy(onRedisException, TreatObjectDisposedExceptionAsTransient);
            if (_retryCount != null)
            {
                return RetryPolicyFactory.GetLinearPolicy(policy.IsTransient, _retryCount.Value);
            }
            else if (ExponentialBackoffConfiguration != null)
            {
                var config = ExponentialBackoffConfiguration;
                return RetryPolicyFactory.GetExponentialPolicy(policy.IsTransient, config.RetryCount, config.MinBackoff, config.MaxBackoff, config.DeltaBackoff);
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
            ExponentialBackoffConfiguration? exponentialBackoffConfiguration = null)
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
            ExponentialBackoffConfiguration = exponentialBackoffConfiguration;
        }
    }
}
