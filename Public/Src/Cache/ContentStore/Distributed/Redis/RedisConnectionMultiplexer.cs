// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using StackExchange.Redis;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Helper class to create a <see cref="ConnectionMultiplexer"/> object
    /// </summary>
    public static class RedisConnectionMultiplexer
    {
        private static readonly Tracer Tracer = new Tracer(nameof(RedisConnectionMultiplexer));

        /// <summary>
        /// Sets a static instance of <see cref="IConnectionMultiplexer"/> used for testing
        /// </summary>
        internal static IConnectionMultiplexer TestConnectionMultiplexer { private get; set; }

        private static readonly ConcurrentDictionary<RedisEndpoint, Lazy<Task<IConnectionMultiplexer>>> Multiplexers = new ConcurrentDictionary<RedisEndpoint, Lazy<Task<IConnectionMultiplexer>>>();

        /// <summary>
        /// Creates a <see cref="IConnectionMultiplexer"/> using given <see cref="IConnectionStringProvider"/>
        /// </summary>
        public static async Task<IConnectionMultiplexer> CreateAsync(Context context, IConnectionStringProvider connectionStringProvider, Severity logSeverity, bool usePreventThreadTheft)
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

            var endpoints = options.GetRedisEndpoint();

            context.Debug($"{nameof(RedisConnectionMultiplexer)}: creating {nameof(RedisConnectionMultiplexer)} for {endpoints}.");

            // Enforce SSL if password is specified. This allows connecting to non password protected local server without SSL
            if (!string.IsNullOrWhiteSpace(options.Password))
            {
                options.Ssl = true;
            }

            var multiplexerTask = Multiplexers.GetOrAdd(
                endpoints,
                _ => new Lazy<Task<IConnectionMultiplexer>>(() => GetConnectionMultiplexerAsync(context, options, logSeverity, usePreventThreadTheft)));

            return await multiplexerTask.Value;
        }

        private static async Task<IConnectionMultiplexer> GetConnectionMultiplexerAsync(Context context, ConfigurationOptions options, Severity logSeverity, bool usePreventThreadTheft)
        {
            var operationContext = new OperationContext(context);
            var endpoint = options.GetRedisEndpoint();

            if (usePreventThreadTheft)
            {
                // See "Thread Theft" article for more details: https://stackexchange.github.io/StackExchange.Redis/ThreadTheft
                // TLDR; when the feature is on all the continuations used inside the library are executed asynchronously.
                ConnectionMultiplexer.SetFeatureFlag("preventthreadtheft", true);
            }

            return await operationContext.PerformNonResultOperationAsync(
                Tracer,
                async () =>
                {
                    context.Debug($"{nameof(RedisConnectionMultiplexer)}: Connecting to redis endpoint: {endpoint}");
                    var result = await CreateMultiplexerCoreAsync(context, options, logSeverity);
                    
                    return result;
                },
                extraEndMessage: r => $"Endpoint: {endpoint}",
                traceOperationStarted: false);
        }

        private static Task<ConnectionMultiplexer> CreateMultiplexerCoreAsync(Context context, ConfigurationOptions options, Severity logSeverity)
        {
            if (logSeverity != Severity.Unknown)
            {
                
                var replacementContext = context.CreateNested(componentName: nameof(RedisConnectionMultiplexer));
                var logger = new TextWriterAdapter(replacementContext, logSeverity, component: "Redis.StackExchange");
                return ConnectionMultiplexer.ConnectAsync(options, logger);
            }

            return ConnectionMultiplexer.ConnectAsync(options);
        }

        private static string AllowAdminIfNeeded(string connectionString)
        {
            // allowAdmin=true option is needed in order to call InfoAsync.
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
        public static async Task ForgetAsync(Context context, ConfigurationOptions options)
        {
            RedisEndpoint endPoints = options.GetRedisEndpoint();
            context.Debug($"Removing {nameof(RedisConnectionMultiplexer)} for endpoint: {endPoints}");
            if (Multiplexers.TryRemove(endPoints, out var multiplexerTask))
            {
                context.Debug($"Closing connection multiplexer. Endpoint: {options.GetRedisEndpoint()}");
                IConnectionMultiplexer multiplexer = await multiplexerTask.Value;
                await multiplexer.CloseAsync(allowCommandsToComplete: true);
                multiplexer.Dispose();
            }
            else
            {
                context.Warning($"Can't find {nameof(RedisConnectionMultiplexer)} for endpoint: {endPoints}");
            }
        }
    }
}
