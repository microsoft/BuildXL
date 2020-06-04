// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using StackExchange.Redis;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Helper class to create a <see cref="ConnectionMultiplexer"/> object
    /// </summary>
    public static class RedisConnectionMultiplexer
    {
        /// <summary>
        /// Sets a static instance of <see cref="IConnectionMultiplexer"/> used for testing
        /// </summary>
        internal static IConnectionMultiplexer TestConnectionMultiplexer { private get; set; }

        private static readonly ConcurrentDictionary<string, Lazy<Task<IConnectionMultiplexer>>> Multiplexers = new ConcurrentDictionary<string, Lazy<Task<IConnectionMultiplexer>>>(StringComparer.Ordinal);

        /// <summary>
        /// Creates a <see cref="IConnectionMultiplexer"/> using given <see cref="IConnectionStringProvider"/>
        /// </summary>
        public static async Task<IConnectionMultiplexer> CreateAsync(Context context, IConnectionStringProvider connectionStringProvider, Severity logSeverity = Severity.Unknown)
        {
            if (TestConnectionMultiplexer != null)
            {
                return TestConnectionMultiplexer;
            }

            var connectionStringResult = await connectionStringProvider.GetConnectionString();
            if (!connectionStringResult.Succeeded || string.IsNullOrWhiteSpace(connectionStringResult.ConnectionString))
            {
                var errorMessage =
                    $"Failed to get connection string from provider {connectionStringProvider.GetType().Name}. {connectionStringResult.ErrorMessage}. Diagnostics: {connectionStringResult.Diagnostics}";
                context.Error(errorMessage);
                throw new ArgumentException(errorMessage, nameof(connectionStringProvider));
            }

            var connectionString = AllowAdminIfNeeded(connectionStringResult.ConnectionString);
            var options = ConfigurationOptions.Parse(connectionString);
            options.ClientName = Environment.MachineName;
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;
            options.SyncTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;

            var endpoints = string.Join(", ", options.EndPoints);

            context.Debug($"RedisConnectionMultiplexer: Connecting to Redis endpoint {endpoints}.");

            // Enforce SSL if password is specified. This allows connecting to non password protected local server without SSL
            if (!string.IsNullOrWhiteSpace(options.Password))
            {
                options.Ssl = true;
            }

            var multiplexerTask = Multiplexers.GetOrAdd(
                endpoints,
                _ => new Lazy<Task<IConnectionMultiplexer>>(() => GetConnectionMultiplexerAsync(context, options, logSeverity)));

            return await multiplexerTask.Value;
        }

        private static async Task<IConnectionMultiplexer> GetConnectionMultiplexerAsync(Context context, ConfigurationOptions options, Severity logSeverity)
        {
            Contract.RequiresNotNull(context);

            if (logSeverity != Severity.Unknown && context.Logger != null)
            {
                var replacementContext = context.CreateNested(componentName: nameof(RedisConnectionMultiplexer));
                var logger = new TextWriterAdapter(replacementContext, logSeverity, component: "Redis.StackExchange");
                return await ConnectionMultiplexer.ConnectAsync(options, logger);
            }

            return await ConnectionMultiplexer.ConnectAsync(options);
        }

        private static string AllowAdminIfNeeded(string connectionString)
        {
            // alloAdmin=true option is needed in order to call InfoAsync.
            string allowAdmin = "allowAdmin=true";
            if (connectionString.IndexOf(allowAdmin, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return connectionString;
            }

            return $"{connectionString},{allowAdmin}";
        }

        /// <summary>
        /// Shutdown and forget a connection multiplexer.
        /// </summary>
        public static async Task ForgetAsync(ConfigurationOptions options)
        {
            if (Multiplexers.TryRemove(options.SslHost, out var multiplexerTask))
            {
                IConnectionMultiplexer multiplexer = await multiplexerTask.Value;
                await multiplexer.CloseAsync(allowCommandsToComplete: true);
                multiplexer.Dispose();
            }
        }
    }
}
