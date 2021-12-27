// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// A factory for creating a redis database
    /// </summary>
    internal interface IRedisDatabaseFactory
    {
        Task<RedisDatabaseAdapter> CreateAsync(OperationContext context, string databaseName, string connectionString);
    }

    /// <nodoc />
    internal record ConfigurableRedisDatabaseFactory(RedisContentLocationStoreConfiguration Configuration) : IRedisDatabaseFactory
    {
        public async Task<RedisDatabaseAdapter> CreateAsync(OperationContext context, string databaseName, string connectionString)
        {
            var factory = await RedisDatabaseFactory.CreateAsync(
                context,
                new LiteralConnectionStringProvider(connectionString),
                Configuration.RedisConnectionMultiplexerConfiguration);

            return CreateDatabase(Configuration, factory, databaseName)!;
        }

        [return: NotNullIfNotNull("factory")]
        public static RedisDatabaseAdapter? CreateDatabase(RedisContentLocationStoreConfiguration configuration, RedisDatabaseFactory? factory, string databaseName, bool optional = false)
        {
            if (factory != null)
            {
                var adapterConfiguration = new RedisDatabaseAdapterConfiguration(
                    configuration.Keyspace,
                    configuration.RedisConnectionErrorLimit,
                    configuration.RedisReconnectionLimitBeforeServiceRestart,
                    databaseName: databaseName,
                    minReconnectInterval: configuration.MinRedisReconnectInterval,
                    cancelBatchWhenMultiplexerIsClosed: configuration.CancelBatchWhenMultiplexerIsClosed,
                    treatObjectDisposedExceptionAsTransient: configuration.TreatObjectDisposedExceptionAsTransient,
                    operationTimeout: configuration.OperationTimeout,
                    exponentialBackoffConfiguration: configuration.ExponentialBackoffConfiguration,
                    retryCount: configuration.RetryCount);

                return new RedisDatabaseAdapter(factory, adapterConfiguration);
            }
            else
            {
                Contract.Assert(optional);
                return null;
            }
        }
    }
}
