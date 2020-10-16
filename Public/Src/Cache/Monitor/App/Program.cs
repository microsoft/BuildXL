// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using System.Diagnostics;
using BuildXL.Cache.ContentStore.Logging;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Monitor.App
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Parser.Run<Program>(args);
        }

        [Verb(IsDefault = true)]
        public static void Run(string? configurationFilePath = null, string? backupFilePath = null, string? logFilePath = null, int? refreshRateMinutes = null, bool debug = false, bool production = false)
        {
            if (debug)
            {
                Debugger.Launch();
            }

            // We reload the application every hour, for two reasons: to reload the stamp monitoring configuration from
            // Kusto, and to reload the configuration (if needed). It's mostly a hack, but it works!
            TimeSpan? refreshRate = null;
            if (refreshRateMinutes.HasValue)
            {
                refreshRate = TimeSpan.FromMinutes(refreshRateMinutes.Value);
            }

            using var programShutdownCancellationTokenSource = new CancellationTokenSource();
            var consoleCancelEventHandler = new ConsoleCancelEventHandler((sender, args) =>
            {
                programShutdownCancellationTokenSource.Cancel();
                args.Cancel = true;
            });

            Console.CancelKeyPress += consoleCancelEventHandler;
            try
            {
                if (refreshRate != null)
                {
                    WithPeriodicCancellationAsync(
                        refreshRate: refreshRate.Value,
                        factory: cancellationToken => RunMonitorAsync(configurationFilePath, backupFilePath, logFilePath, production, cancellationTokenSource: null, cancellationToken: cancellationToken),
                        cancellationToken: programShutdownCancellationTokenSource.Token).Wait();
                }
                else
                {
                    WithInternalCancellationAsync(
                        factory: (cancellationTokenSource, cancellationToken) => RunMonitorAsync(configurationFilePath, backupFilePath, logFilePath, production, cancellationTokenSource, cancellationToken),
                        cancellationToken: programShutdownCancellationTokenSource.Token).Wait();
                }
            }
            finally
            {
                Console.CancelKeyPress -= consoleCancelEventHandler;
            }
        }

        private static async Task WithInternalCancellationAsync(Func<CancellationTokenSource, CancellationToken, Task> factory, CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"Starting application with internal refresh");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                try
                {
                    await factory(cts, cts.Token);
                    Console.WriteLine($"Application terminated successfully");
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception e)
                {
                    Console.WriteLine($"Exception found trying to restart the application: {e}");
                    break;
                }
#pragma warning restore CA1031 // Do not catch general exception types
            }
        }

        private static async Task WithPeriodicCancellationAsync(TimeSpan refreshRate, Func<CancellationToken, Task> factory, CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"Starting application with refresh in `{refreshRate}`");
                using var refresh = new CancellationTokenSource(refreshRate);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(refresh.Token, cancellationToken);

                try
                {
                    await factory(cts.Token);
                    Console.WriteLine($"Application terminated successfully");
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception e)
                {
                    Console.WriteLine($"Exception found trying to restart the application: {e}");
                    break;
                }
#pragma warning restore CA1031 // Do not catch general exception types
            }
        }

        private static async Task RunMonitorAsync(string? configurationFilePath, string? backupFilePath, string? logFilePath, bool production, CancellationTokenSource? cancellationTokenSource = null, CancellationToken cancellationToken = default)
        {
            var configuration = LoadConfiguration(production, configurationFilePath);
            if (!string.IsNullOrEmpty(backupFilePath))
            {
                // Needed to satisfy the type checker
                Contract.AssertNotNull(backupFilePath);
                PersistConfiguration(configuration, backupFilePath, force: true);
            }

            Action? onWatchlistUpdated = null;
            if (cancellationTokenSource != null)
            {
                onWatchlistUpdated = () =>
                {
                    cancellationTokenSource.Cancel();
                };
            }

            await WithLoggerAsync(async (logger) => {
                using (var monitor = await Monitor.CreateAsync(configuration, logger).ThrowIfFailureAsync())
                {
                    await monitor.RunAsync(onWatchlistUpdated, cancellationToken);
                }
            }, logFilePath);
        }

        private static async Task WithLoggerAsync(Func<ILogger, Task> action, string? logFilePath)
        {
            CsvFileLog? csvFileLog = null;
            ConsoleLog? consoleLog = null;
            Logger? logger = null;

            try
            {
                if (!string.IsNullOrEmpty(logFilePath))
                {
                    // Needed to satisfy the type checker
                    Contract.AssertNotNull(logFilePath);

                    if (string.IsNullOrEmpty(Path.GetDirectoryName(logFilePath)))
                    {
                        var cwd = Directory.GetCurrentDirectory();
                        logFilePath = Path.Combine(cwd, logFilePath);
                    }

                    csvFileLog = new CsvFileLog(logFilePath, new List<CsvFileLog.ColumnKind>()
                                {
                                    CsvFileLog.ColumnKind.PreciseTimeStamp,
                                    CsvFileLog.ColumnKind.ProcessId,
                                    CsvFileLog.ColumnKind.ThreadId,
                                    CsvFileLog.ColumnKind.LogLevel,
                                    CsvFileLog.ColumnKind.LogLevelFriendly,
                                    CsvFileLog.ColumnKind.Message,
                                });
                }

                consoleLog = new ConsoleLog(useShortLayout: false, printSeverity: true);

                var logs = new ILog?[] { csvFileLog, consoleLog };
                logger = new Logger(logs.Where(log => log != null).Cast<ILog>().ToArray());

                await action(logger);
            }
            finally
            {
                logger?.Dispose();
                csvFileLog?.Dispose();
                consoleLog?.Dispose();
            }
        }

        private static Monitor.Configuration LoadConfiguration(bool production, string? configurationFilePath = null)
        {
            Monitor.Configuration? configuration = null;

            if (string.IsNullOrEmpty(configurationFilePath))
            {
                configuration = new Monitor.Configuration();

                var applicationKey = Environment.GetEnvironmentVariable("CACHE_MONITOR_APPLICATION_KEY");
                if (string.IsNullOrEmpty(applicationKey))
                {
                    throw new ArgumentException($"Please specify a configuration file or set the `CACHE_MONITOR_APPLICATION_KEY` environment variable to your application key");
                }
                configuration.AzureAppKey = applicationKey;

                configuration.TestMode = !production;
                return configuration;
            }

            if (!File.Exists(configurationFilePath))
            {
                throw new ArgumentException($"Configuration file does not exist at `{configurationFilePath}`",
                    nameof(configurationFilePath));
            }

            using var stream = File.OpenText(configurationFilePath);
            var serializer = CreateSerializer();

            try
            {
                configuration = (Monitor.Configuration?)serializer.Deserialize(stream, typeof(Monitor.Configuration));
            }
            catch (JsonException exception)
            {
                throw new ArgumentException($"Invalid configuration file at `{configurationFilePath}`",
                    nameof(configurationFilePath),
                    exception);
            }

            if (configuration is null)
            {
                throw new ArgumentException($"Configuration file is empty at `{configurationFilePath}`",
                    nameof(configurationFilePath));
            }

            if (configuration.TestMode == production)
            {
                throw new ArgumentException($"TestMode is set to {configuration.TestMode}, but should not be equal to {production}");
            }

            return configuration;
        }

        private static void PersistConfiguration(Monitor.Configuration configuration, string configurationFilePath, bool force = false)
        {
            Contract.RequiresNotNullOrEmpty(configurationFilePath);

            if (File.Exists(configurationFilePath) && !force)
            {
                throw new ArgumentException($"File `{configurationFilePath}` already exists. Not overwriting it.",
                    nameof(configurationFilePath));
            }

            using var stream = File.CreateText(configurationFilePath);
            var serializer = CreateSerializer();
            serializer.Serialize(stream, configuration);
        }

        private static JsonSerializer CreateSerializer()
        {
            return new JsonSerializer
            {
                Formatting = Formatting.Indented,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            };
        }
    }
}
