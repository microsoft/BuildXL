// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Utilities;
#if MICROSOFT_INTERNAL
using Microsoft.Caching.Redis;
using Microsoft.Caching.Redis.KeyspaceIsolation;
#else
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
#endif

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Creates and returns a Redis Database
    /// </summary>
    public class RedisDatabaseFactory
    {
        private static readonly Tracer Tracer = new Tracer(nameof(RedisDatabaseFactory));

        private readonly SemaphoreSlim _creationSemaphore = new SemaphoreSlim(1, 1);

        private readonly Func<Task<IConnectionMultiplexer>> _connectionMultiplexerFactory;
        private readonly Func<IConnectionMultiplexer, Task> _connectionMultiplexerShutdownFunc;
        private AsyncLazy<IConnectionMultiplexer> _connectionMultiplexer;

        /// <summary>
        /// Notifies existing clients that the current connection multiplexer instance is about to be closed.
        /// Also used internally to detect that the multiplexer must be closed.
        /// </summary>
        private CancellationTokenSource _resetConnectionMultiplexerCts;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisDatabaseFactory"/> class.
        /// </summary>
        private RedisDatabaseFactory(Func<Task<IConnectionMultiplexer>> connectionMultiplexerFactory, Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc)
        {
            _connectionMultiplexerFactory = connectionMultiplexerFactory;
            _connectionMultiplexerShutdownFunc = connectionMultiplexerShutdownFunc;
            _connectionMultiplexer = new AsyncLazy<IConnectionMultiplexer>(connectionMultiplexerFactory);
            _resetConnectionMultiplexerCts = new CancellationTokenSource();
        }

        /// <summary>
        /// Factory method for a database factory.
        /// </summary>
        public static Task<RedisDatabaseFactory> CreateAsync(Context context, IConnectionStringProvider provider, Severity logSeverity, bool usePreventThreadTheft)
        {
            return CreateAsync(
                context,
                provider,
                new RedisConnectionMultiplexerConfiguration(logSeverity, usePreventThreadTheft));
        }

        /// <summary>
        /// Factory method for a database factory.
        /// </summary>
        public static RedisDatabaseFactory Create(Context context, IConnectionStringProvider provider, RedisConnectionMultiplexerConfiguration configuration)
        {
            Func<Task<IConnectionMultiplexer>> connectionMultiplexerFactory = () => RedisConnectionMultiplexer.CreateAsync(context, provider, configuration);

            Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc = async m =>
            {
                ConfigurationOptions options = ConfigurationOptions.Parse(m.Configuration);
                await RedisConnectionMultiplexer.ForgetAsync(context, options);
            };

            return new RedisDatabaseFactory(connectionMultiplexerFactory, connectionMultiplexerShutdownFunc);
        }

        /// <summary>
        /// Factory method for a database factory.
        /// </summary>
        public static async Task<RedisDatabaseFactory> CreateAsync(Context context, IConnectionStringProvider provider, RedisConnectionMultiplexerConfiguration configuration)
        {
            var databaseFactory = Create(context, provider, configuration);
            await databaseFactory.InitializeAsync();
            return databaseFactory;
        }

        /// <summary>
        /// Factory method for a database factory.
        /// </summary>
        /// <remarks>
        /// Used by tests only.
        /// </remarks>
        public static Task<RedisDatabaseFactory> CreateAsync(IConnectionStringProvider provider, IConnectionMultiplexer connectionMultiplexer)
        {
            return CreateAsync(() => connectionMultiplexer, m => BoolResult.SuccessTask);
        }

        /// <summary>
        /// Factory method for a database factory used by tests only.
        /// </summary>
        /// <remarks>
        /// Used by tests only.
        /// </remarks>
        public static async Task<RedisDatabaseFactory> CreateAsync(Func<IConnectionMultiplexer> connectionMultiplexerFactory, Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc)
        {
            var databaseFactory = new RedisDatabaseFactory(() => Task.FromResult(connectionMultiplexerFactory()), connectionMultiplexerShutdownFunc);
            await databaseFactory.InitializeAsync();
            return databaseFactory;
        }

        private Task InitializeAsync()
        {
            return _connectionMultiplexer.GetValueAsync();
        }

        /// <nodoc />
        public EndPoint[] GetEndPoints() => _connectionMultiplexer.Value.GetEndPoints();

        /// <nodoc />
        public IServer GetServer(EndPoint endpoint) => _connectionMultiplexer.Value.GetServer(endpoint);

        /// <summary>
        /// Gets a Redis Database object with a specified key prefix.
        /// </summary>
        public async Task<(IDatabase database, CancellationToken databaseLifetimeToken)> GetDatabaseWithKeyPrefix(Context context, string keySpace)
        {
            var connectionMultiplexer = await _connectionMultiplexer.GetValueAsync();

            if (_resetConnectionMultiplexerCts.IsCancellationRequested)
            {
                using (await SemaphoreSlimToken.WaitAsync(_creationSemaphore))
                {
                    if (_resetConnectionMultiplexerCts.IsCancellationRequested)
                    {
                        Tracer.Debug(context, "Shutting down current connection multiplexer.");

                        await _connectionMultiplexerShutdownFunc(connectionMultiplexer);

                        Tracer.Debug(context, "Creating new multiplexer instance.");

                        var newConnectionMultiplexer = await _connectionMultiplexerFactory();
                        connectionMultiplexer = newConnectionMultiplexer;

                        // Using volatile operation to prevent the instruction reordering.
                        // We really want the cts change to be the last one.
                        Volatile.Write(ref _connectionMultiplexer, AsyncLazy<IConnectionMultiplexer>.FromResult(newConnectionMultiplexer));

                        // Need to change the source at the end to avoid the race condition when
                        // another thread can see a non-canceled CancellationTokenSource
                        // and at the end still get an old connection multiplexer.
                        _resetConnectionMultiplexerCts = new CancellationTokenSource();
                    }
                }
            }

            return (connectionMultiplexer.GetDatabase().WithKeyPrefix(keySpace), _resetConnectionMultiplexerCts.Token);
        }

        /// <summary>
        /// Informs the database factory to reset and reconnect its connection multiplexer.
        /// </summary>
        public void ResetConnectionMultiplexer()
        {
            // Cancelling all the pending operations.
            _resetConnectionMultiplexerCts.Cancel();
        }
    }
}
