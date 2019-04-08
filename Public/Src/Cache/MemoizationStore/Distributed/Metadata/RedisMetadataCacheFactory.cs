// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata.Tracing;
using BuildXL.Cache.MemoizationStore.Distributed.Utils;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;

namespace BuildXL.Cache.MemoizationStore.Distributed.Metadata
{
    /// <summary>
    /// Factory class for constructing <see cref="RedisMetadataCache"/> using known connection string providers
    /// </summary>
    public static class RedisMetadataCacheFactory
    {
        /// <summary>
        /// Default keyspace for Redis
        /// </summary>
        public const string DefaultKeySpace = "Default:";

        /// <summary>
        /// Salt to determine keyspace's current version
        /// </summary>
        public const string Salt = "V2";

        /// <summary>
        /// Creates an instance of <see cref="RedisMetadataCache"/>
        /// </summary>
        public static IMetadataCache Create(IMetadataCacheTracer tracer, string keySpace = DefaultKeySpace, TimeSpan? cacheKeyBumpTime = null)
        {
            var connectionStringProvider = IdentifyConnectionStringProvider();
            return Create(connectionStringProvider, tracer, keySpace, cacheKeyBumpTime);
        }

        /// <summary>
        /// Creates an instance of <see cref="RedisMetadataCache"/>
        /// </summary>
        public static IMetadataCache Create(IConnectionStringProvider connectionStringProvider, IMetadataCacheTracer tracer, string keySpace = DefaultKeySpace, TimeSpan? cacheKeyBumpTime = null)
        {
            return new RedisMetadataCache(connectionStringProvider, new RedisSerializer(), keySpace + Salt, tracer, cacheKeyBumpTime);
        }

        private static IConnectionStringProvider IdentifyConnectionStringProvider()
        {
            // 1. Try to get connection string from environment variable (usually for testing)
            var redisConnectionString = Environment.GetEnvironmentVariable(EnvironmentConnectionStringProvider.RedisConnectionStringEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(redisConnectionString))
            {
                return new EnvironmentConnectionStringProvider(EnvironmentConnectionStringProvider.RedisConnectionStringEnvironmentVariable);
            }

            // 2. Check if credential provider exists
            var credentialProvider = Environment.GetEnvironmentVariable(ExecutableConnectionStringProvider.CredentialProviderVariableName);
            if (!string.IsNullOrWhiteSpace(credentialProvider))
            {
                return new ExecutableConnectionStringProvider(ExecutableConnectionStringProvider.RedisConnectionIntent.Metadata);
            }

            throw new InvalidOperationException($"None of expected environment variables defined: [{ExecutableConnectionStringProvider.CredentialProviderVariableName}, {EnvironmentConnectionStringProvider.RedisConnectionStringEnvironmentVariable}]");
        }
    }
}
