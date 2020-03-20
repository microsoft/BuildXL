// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        public int RedisConnectionErrorLimit { get; }

        public bool TraceOperationFailures { get; }

        public bool TraceTransientFailures { get; }

        public string DatabaseName { get; }

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
            bool traceOperationFailures = false,
            bool traceTransientFailures = false,
            int? retryCount = null,
            string? databaseName = null)
        {
            _retryCount = retryCount;
            KeySpace = keySpace;
            RedisConnectionErrorLimit = redisConnectionErrorLimit;
            TraceOperationFailures = traceOperationFailures;
            TraceTransientFailures = traceTransientFailures;
            DatabaseName = databaseName ?? "Default";
        }
    }
}
