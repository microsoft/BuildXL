// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Practices.TransientFaultHandling;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <nodoc />
    public class ExponentialBackoffConfiguration
    {
        /// <summary>
        /// The value is: <code>TimeSpan.FromSeconds(1.0)</code>
        /// </summary>
        public static TimeSpan DefaultMinBackoff { get; } = RetryStrategy.DefaultMinBackoff;

        /// <summary>
        /// The value is: <code>TimeSpan.FromSeconds(30.0)</code>
        /// </summary>
        public static TimeSpan DefaultMaxBackoff { get; } = RetryStrategy.DefaultMaxBackoff;

        /// <summary>
        /// The value is: <code>TimeSpan.FromSeconds(10.0)</code>
        /// </summary>
        public static TimeSpan DefaultDeltaBackoff { get; } = RetryStrategy.DefaultClientBackoff;

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
            MinBackoff = minBackoff ?? DefaultMinBackoff;
            MaxBackoff = maxBackoff ?? DefaultMaxBackoff;
            DeltaBackoff = deltaBackoff ?? DefaultDeltaBackoff;
        }

        /// <nodoc />
        public ExponentialBackoff CreateRetryStrategy() => new ExponentialBackoff(RetryCount, MinBackoff, MaxBackoff, DeltaBackoff);
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

        public RetryPolicy CreateRetryPolicy(Action<Exception> onRedidException)
        {
            if (_retryCount != null)
            {
                return new RetryPolicy(new RedisDatabaseAdapter.RedisRetryPolicy(onRedidException, TreatObjectDisposedExceptionAsTransient), _retryCount.Value);
            }
            else if (ExponentialBackoffConfiguration != null)
            {
                return new RetryPolicy(new RedisDatabaseAdapter.RedisRetryPolicy(onRedidException, TreatObjectDisposedExceptionAsTransient), ExponentialBackoffConfiguration.CreateRetryStrategy());
            }
            else
            {
                return new RetryPolicy(new RedisDatabaseAdapter.RedisRetryPolicy(onRedidException, TreatObjectDisposedExceptionAsTransient), RetryStrategy.DefaultExponential);
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
