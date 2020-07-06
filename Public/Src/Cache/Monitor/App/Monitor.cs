using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.App.Rules;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using Kusto.Ingest;
using static BuildXL.Cache.ContentStore.Logging.CsvFileLog;

#nullable enable

namespace BuildXL.Cache.Monitor.App
{
    internal class Monitor : IDisposable
    {
        internal static readonly Dictionary<Env, string> EnvironmentToKustoDatabaseName = new Dictionary<Env, string>()
            {
                { Env.Production, "CloudBuildProd" },
                { Env.Test, "CloudBuildCBTest" },
                { Env.CI, "CloudBuildCI" },
            };

        public class Configuration
        {
            public string LogFilePath { get; set; } = @"Monitor.log";

            public string KustoClusterUrl { get; set; } = "https://cbuild.kusto.windows.net";

            public string KustoIngestionClusterUrl { get; set; } = "https://cbuild.kusto.windows.net";

            public string Authority { get; set; } = "72f988bf-86f1-41af-91ab-2d7cd011db47";

            public string ApplicationClientId { get; set; } = "22cabbbb-1f32-4057-b601-225bab98348d";

            public string? ApplicationKey { get; set; } = null;

            public KustoWriter<Notification>.Configuration KustoNotifier { get; set; } = new KustoWriter<Notification>.Configuration()
            {
                KustoDatabaseName = "CloudBuildCBTest",
                KustoTableName = "BuildXLCacheMonitor",
                KustoTableIngestionMappingName = "MonitorIngestionMapping",
            };

            public RuleScheduler.Configuration Scheduler { get; set; } = new RuleScheduler.Configuration()
            {
                PersistStatePath = @"SchedulerState.json",
                PersistClearFailedEntriesOnLoad = true,
                MaximumConcurrency = 10,
            };

            /// <summary>
            /// The scheduler writes out logs to the logger, but also writes information about rules that were run
            /// into Kusto. This is used to know when an event has passed (i.e. if a rule has been run again since
            /// the last check and didn't produce a notification, that means the event has passed).
            /// </summary>
            public KustoWriter<RuleScheduler.LogEntry>.Configuration SchedulerKustoNotifier { get; set; } = new KustoWriter<RuleScheduler.LogEntry>.Configuration()
            {
                KustoDatabaseName = "CloudBuildCBTest",
                KustoTableName = "BuildXLCacheMonitorSchedulerLog",
                KustoTableIngestionMappingName = "MonitorIngestionMapping",
            };
        }

        private readonly Configuration _configuration;

        private readonly IClock _clock = SystemClock.Instance;

        private readonly Logger _logger;
        private CsvFileLog? _csvFileLog = null;
        private ConsoleLog? _consoleLog = null;

        private readonly RuleScheduler _scheduler;
        private readonly KustoWriter<Notification> _alertNotifier;
        private readonly KustoWriter<RuleScheduler.LogEntry> _schedulerLogWriter;

        private readonly IKustoIngestClient _kustoIngestClient;
        private readonly ICslQueryProvider _cslQueryProvider;

        public Monitor(Configuration configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;

            _logger = CreateLogger();

            Contract.RequiresNotNullOrEmpty(_configuration.KustoIngestionClusterUrl);
            Contract.RequiresNotNullOrEmpty(_configuration.ApplicationClientId);
            Contract.RequiresNotNullOrEmpty(_configuration.ApplicationKey);
            Contract.RequiresNotNullOrEmpty(_configuration.Authority);
            var kustoIngestConnectionString = new KustoConnectionStringBuilder(_configuration.KustoIngestionClusterUrl)
                .WithAadApplicationKeyAuthentication(_configuration.ApplicationClientId, _configuration.ApplicationKey, _configuration.Authority);
            _kustoIngestClient = KustoIngestFactory.CreateDirectIngestClient(kustoIngestConnectionString);

            var kustoConnectionString = new KustoConnectionStringBuilder(_configuration.KustoClusterUrl)
                .WithAadApplicationKeyAuthentication(_configuration.ApplicationClientId, _configuration.ApplicationKey, _configuration.Authority);
            _cslQueryProvider = KustoClientFactory.CreateCslQueryProvider(kustoConnectionString);

            Contract.RequiresNotNull(_configuration.KustoNotifier);
            _alertNotifier = new KustoWriter<Notification>(_configuration.KustoNotifier, _logger, _kustoIngestClient);

            Contract.RequiresNotNull(_configuration.SchedulerKustoNotifier);
            _schedulerLogWriter = new KustoWriter<RuleScheduler.LogEntry>(_configuration.SchedulerKustoNotifier, _logger, _kustoIngestClient);

            _scheduler = new RuleScheduler(_configuration.Scheduler, _logger, _clock, _schedulerLogWriter);
        }

        private Logger CreateLogger()
        {
            var logs = new List<ILog>();

            if (!string.IsNullOrEmpty(_configuration.LogFilePath))
            {
                var logFilePath = _configuration.LogFilePath;

                if (string.IsNullOrEmpty(Path.GetDirectoryName(logFilePath)))
                {
                    var cwd = Directory.GetCurrentDirectory();
                    logFilePath = Path.Combine(cwd, logFilePath);
                }

                _csvFileLog = new CsvFileLog(logFilePath, new List<ColumnKind>() {
                    ColumnKind.PreciseTimeStamp,
                    ColumnKind.ProcessId,
                    ColumnKind.ThreadId,
                    ColumnKind.LogLevel,
                    ColumnKind.LogLevelFriendly,
                    ColumnKind.Message,
                });

                logs.Add(_csvFileLog);
            }

            // NOTE(jubayard): making sure things get logged to file first
            _consoleLog = new ConsoleLog(useShortLayout: false, printSeverity: true);
            logs.Add(_consoleLog);

            return new Logger(logs.ToArray());
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            var watchlist = await Watchlist.CreateAsync(_logger, _cslQueryProvider);
            CreateSchedule(watchlist);
            await _scheduler.RunAsync(cancellationToken);
        }

        private class Instantiation
        {
            public IRule? Rule { get; set; }

            public TimeSpan PollingPeriod { get; set; }

            public bool ForceRun { get; set; } = false;
        };

        /// <summary>
        /// Creates the schedule of rules that will be run. Also responsible for configuring them.
        /// </summary>
        private void CreateSchedule(Watchlist watchlist)
        {
            // TODO: single query for all stamps in rules that support it. This should significantly improve performance.
            // TODO: per-stamp configuration (some stamps are more important than others, query frequency should reflect that)
            // TODO: query weight (how much does it cost). We should adapt scheduling policy to have lighter queries prioritize earlier than the others.
            // TODO: stamp configuration knowledge. Stamp configuration affects what our thresholds should be. We should reflect that here.
            // TODO: add jitter to rules, so that queries to Kusto are spread out over time instead of all at once
            OncePerStamp(baseConfiguration =>
            {
                var configuration = new LastProducedCheckpointRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation()
                {
                    Rule = new LastProducedCheckpointRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(40),
                });
            }, watchlist);

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new LastRestoredCheckpointRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation()
                {
                    Rule = new LastRestoredCheckpointRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(30),
                });
            }, watchlist);

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new CheckpointSizeRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation()
                {
                    Rule = new CheckpointSizeRule(configuration),
                    PollingPeriod = configuration.AnomalyDetectionHorizon - TimeSpan.FromMinutes(5),
                });
            }, watchlist);

            // TODO: this rule is too noisy and inaccurate, we should make it work again
            //OncePerStamp(baseConfiguration =>
            //{
            //    var configuration = new ActiveMachinesRule.Configuration(baseConfiguration);
            //    return Utilities.Yield(new Instantiation()
            //    {
            //        Rule = new ActiveMachinesRule(configuration),
            //        PollingPeriod = configuration.AnomalyDetectionHorizon - TimeSpan.FromMinutes(5),
            //    });
            //}, watchlist);

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new EventHubProcessingDelayRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation()
                {
                    Rule = new EventHubProcessingDelayRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(30),
                });
            }, watchlist);

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new BuildFailuresRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation()
                {
                    Rule = new BuildFailuresRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(45),
                });
            }, watchlist);

            // TODO: fire-and-forget exceptions are now being reported on the dashboards. We should see if this can be recycled.
            //OncePerStamp(baseConfiguration =>
            //{
            //    var configuration = new FireAndForgetExceptionsRule.Configuration(baseConfiguration);
            //    return Utilities.Yield(new Instantiation()
            //    {
            //        Rule = new FireAndForgetExceptionsRule(configuration),
            //        PollingPeriod = configuration.LookbackPeriod - TimeSpan.FromMinutes(5),
            //    });
            //}, watchlist);

            // TODO: this was just too noisy
            //OncePerStamp(baseConfiguration =>
            //{
            //    var configuration = new ContractViolationsRule.Configuration(baseConfiguration);
            //    return Utilities.Yield(new Instantiation() {
            //        Rule = new ContractViolationsRule(configuration),
            //        PollingPeriod = configuration.LookbackPeriod,
            //    });
            //}, watchlist);

            var failureChecks = new List<OperationFailureCheckRule.Check>() {
                new OperationFailureCheckRule.Check()
                {
                    Match = "StartupAsync",
                },
                new OperationFailureCheckRule.Check()
                {
                    Match = "ShutdownAsync",
                },
                new OperationFailureCheckRule.Check()
                {
                    Match = "RestoreCheckpointAsync",
                },
                new OperationFailureCheckRule.Check()
                {
                    Match = "CreateCheckpointAsync",
                },
                new OperationFailureCheckRule.Check()
                {
                    Match = "ReconcileAsync",
                },
                new OperationFailureCheckRule.Check()
                {
                    Match = "ProcessEventsCoreAsync",
                },
                new OperationFailureCheckRule.Check()
                {
                    // TODO(jubayard): lower severity
                    Match = "SendEventsCoreAsync",
                },
            };

            OncePerStamp(baseConfiguration =>
            {
                return failureChecks.Select(check =>
                {
                    var configuration = new OperationFailureCheckRule.Configuration(baseConfiguration)
                    {
                        Check = check,
                    };

                    return new Instantiation()
                    {
                        Rule = new OperationFailureCheckRule(configuration),
                        PollingPeriod = configuration.LookbackPeriod - TimeSpan.FromMinutes(5),
                    };
                });
            }, watchlist);

            var performanceChecks = new List<OperationPerformanceOutliersRule.DynamicCheck>() {
                new OperationPerformanceOutliersRule.DynamicCheck()
                {
                    LookbackPeriod = TimeSpan.FromMinutes(60),
                    DetectionPeriod = TimeSpan.FromMinutes(30),
                    Match = "LocalCacheServer.StartupAsync",
                    Constraint = $"TimeMs >= {TimeSpan.FromMinutes(1).TotalMilliseconds}",
                },
                new OperationPerformanceOutliersRule.DynamicCheck()
                {
                    LookbackPeriod = TimeSpan.FromHours(12),
                    DetectionPeriod = TimeSpan.FromHours(1),
                    Match = "CheckpointManager.CreateCheckpointAsync",
                    Constraint = $"TimeMs >= {TimeSpan.FromMinutes(1).TotalMilliseconds}",
                },
                new OperationPerformanceOutliersRule.DynamicCheck()
                {
                    LookbackPeriod = TimeSpan.FromHours(12),
                    DetectionPeriod = TimeSpan.FromHours(1),
                    Match = "CheckpointManager.RestoreCheckpointAsync",
                    Constraint = $"TimeMs >= P95 and P95 >= {TimeSpan.FromMinutes(30).TotalMilliseconds}",
                },
            };

            OncePerStamp(baseConfiguration =>
            {
                return performanceChecks.Select(check =>
                {
                    var configuration = new OperationPerformanceOutliersRule.Configuration(baseConfiguration)
                    {
                        Check = check,
                    };

                    return new Instantiation()
                    {
                        Rule = new OperationPerformanceOutliersRule(configuration),
                        PollingPeriod = check.DetectionPeriod - TimeSpan.FromMinutes(5),
                    };
                });
            }, watchlist);

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new ServiceRestartsRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation()
                {
                    Rule = new ServiceRestartsRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(30),
                });
            }, watchlist);
        }

        /// <summary>
        /// Schedules a rule to be run over different stamps and environments.
        /// </summary>
        private void OncePerStamp(Func<KustoRuleConfiguration, IEnumerable<Instantiation>> generator, Watchlist watchlist)
        {
            foreach (var entry in watchlist.Entries)
            {
                var tableNameFound = watchlist.TryGetCacheTableName(entry, out var cacheTableName);
                Contract.Assert(tableNameFound);

                var configuration = new KustoRuleConfiguration()
                {
                    Clock = _clock,
                    Logger = _logger,
                    Notifier = _alertNotifier,
                    CslQueryProvider = _cslQueryProvider,
                    KustoDatabaseName = EnvironmentToKustoDatabaseName[entry.Environment],
                    Environment = entry.Environment,
                    Stamp = entry.Stamp,
                    CacheTableName = cacheTableName,
                };

                foreach (var rule in generator(configuration))
                {
                    Contract.AssertNotNull(rule.Rule);
                    _scheduler.Add(rule.Rule, rule.PollingPeriod, rule.ForceRun);
                }
            }
        }

        #region IDisposable Support
        private bool _disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _scheduler?.Dispose();
                    _schedulerLogWriter?.Dispose();
                    _alertNotifier?.Dispose();
                    _kustoIngestClient?.Dispose();
                    _cslQueryProvider?.Dispose();
                    _logger?.Dispose();
                    _csvFileLog?.Dispose();
                    _consoleLog?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose() => Dispose(true);
        #endregion


    }
}
