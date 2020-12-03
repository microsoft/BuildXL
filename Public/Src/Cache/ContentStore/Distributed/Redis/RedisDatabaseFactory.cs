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
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

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
        private IConnectionMultiplexer _connectionMultiplexer;

        /// <summary>
        /// Notifies existing clients that the current connection multiplexer instance is about to be closed.
        /// Also used internally to detect that the multiplexer must be closed.
        /// </summary>
        private CancellationTokenSource _resetConnectionMultiplexerCts;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisDatabaseFactory"/> class.
        /// </summary>
        private RedisDatabaseFactory(IConnectionMultiplexer connectionMultiplexer, Func<Task<IConnectionMultiplexer>> connectionMultiplexerFactory, Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc)
        {
            _connectionMultiplexerFactory = connectionMultiplexerFactory;
            _connectionMultiplexerShutdownFunc = connectionMultiplexerShutdownFunc;
            _connectionMultiplexer = connectionMultiplexer;
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
        public static async Task<RedisDatabaseFactory> CreateAsync(Context context, IConnectionStringProvider provider, RedisConnectionMultiplexerConfiguration configuration)
        {
            Func<Task<IConnectionMultiplexer>> connectionMultiplexerFactory = () => RedisConnectionMultiplexer.CreateAsync(context, provider, configuration);

            Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc = async m =>
            {
                ConfigurationOptions options = ConfigurationOptions.Parse(m.Configuration);
                await RedisConnectionMultiplexer.ForgetAsync(context, options);
            };

            var connectionMultiplexer = await connectionMultiplexerFactory();
            return new RedisDatabaseFactory(connectionMultiplexer, connectionMultiplexerFactory, connectionMultiplexerShutdownFunc);
        }

        /// <summary>
        /// Factory method for a database factory.
        /// </summary>
        /// <remarks>
        /// Used by tests only.
        /// </remarks>
        public static Task<RedisDatabaseFactory> CreateAsync(IConnectionStringProvider provider, IConnectionMultiplexer connectionMultiplexer)
        {
            Func<Task<IConnectionMultiplexer>> connectionMultiplexerFactory = () => Task.FromResult(connectionMultiplexer);

            Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc = m => BoolResult.SuccessTask;

            var databaseFactory = new RedisDatabaseFactory(connectionMultiplexer, connectionMultiplexerFactory, connectionMultiplexerShutdownFunc);
            return Task.FromResult(databaseFactory);
        }

        /// <summary>
        /// Factory method for a database factory used by tests only.
        /// </summary>
        /// <remarks>
        /// Used by tests only.
        /// </remarks>
        public static Task<RedisDatabaseFactory> CreateAsync(Func<IConnectionMultiplexer> connectionMultiplexerFactory, Func<IConnectionMultiplexer, Task> connectionMultiplexerShutdownFunc)
        {
            var connectionMultiplexer = connectionMultiplexerFactory();
            var databaseFactory = new RedisDatabaseFactory(connectionMultiplexer, () => Task.FromResult(connectionMultiplexerFactory()), connectionMultiplexerShutdownFunc);
            return Task.FromResult(databaseFactory);
        }

        /// <nodoc />
        public EndPoint[] GetEndPoints() => _connectionMultiplexer.GetEndPoints();

        /// <nodoc />
        public IServer GetServer(EndPoint endpoint) => _connectionMultiplexer.GetServer(endpoint);

        /// <summary>
        /// Gets a Redis Database object with a specified key prefix.
        /// </summary>
        public async Task<(IDatabase database, CancellationToken databaseLifetimeToken)> GetDatabaseWithKeyPrefix(Context context, string keySpace)
        {
            if (_resetConnectionMultiplexerCts.IsCancellationRequested)
            {
                using (await SemaphoreSlimToken.WaitAsync(_creationSemaphore))
                {
                    if (_resetConnectionMultiplexerCts.IsCancellationRequested)
                    {
                        Tracer.Debug(context, "Shutting down current connection multiplexer.");

                        await _connectionMultiplexerShutdownFunc(_connectionMultiplexer);

                        Tracer.Debug(context, "Creating new multiplexer instance.");

                        var newConnectionMultiplexer = await _connectionMultiplexerFactory();

                        // Using volatile operation to prevent the instruction reordering.
                        // We really want the cts change to be the last one.
                        Volatile.Write(ref _connectionMultiplexer, newConnectionMultiplexer);

                        // Need to change the source at the end to avoid the race condition when
                        // another thread can see a non-canceled CancellationTokenSource
                        // and at the end still get an old connection multiplexer.
                        _resetConnectionMultiplexerCts = new CancellationTokenSource();
                    }
                }
            }

            return (_connectionMultiplexer.GetDatabase().WithKeyPrefix(keySpace), _resetConnectionMultiplexerCts.Token);
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
