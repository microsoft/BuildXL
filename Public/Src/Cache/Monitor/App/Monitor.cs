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

namespace BuildXL.Cache.Monitor.App
{
    internal class Monitor : IDisposable
    {
        private static readonly Dictionary<Env, string> EnvironmentToKustoDatabaseName = new Dictionary<Env, string>()
            {
                { Env.Production, "CloudBuildProd" },
                { Env.Test, "CloudBuildCBTest" },
                { Env.CI, "CloudBuildCI" },
            };

        private static readonly Dictionary<Env, IReadOnlyList<string>> Stamps = new Dictionary<Env, IReadOnlyList<string>>()
            {
                {
                    Env.Test, new [] {
                        "DM_S1",
                        "DM_S2",
                    }
                },
                {
                    Env.Production, new string[] {
                        "BN",
                        "BN_PS01",
                        "BN_PS02",
                        "BN_S2",
                        "BN_S3",
                        "DM",
                        "DM_PS01",
                        "DM_PS02",
                        "DM_PS03",
                        "DM_PS04",
                        "DM_PS05",
                        "DM_PS06",
                        "DM_PS07",
                        "DM_PS08",
                        "DM_PS09",
                        "DM_S2",
                        "DM_S3",
                        "MW_PS01",
                        "MW_PS02",
                        "MW_PS03",
                        //"MW_PS04",
                        //"MW_PS05",
                        "MW_PS06",
                        "MW_PS07",
                        "MW_S1",
                        "MW_S2",
                        "MW_S3",
                        "MW_S4",
                        "MW_S5",
                        "MW_S6",
                        "MW_S7",
                        "MW_S8",
                        "MW_S9",
                        "SN_S1",
                        "SN_S2",
                        "SN_S3",
                        "SN_S4",
                        "SN_S5",
                        "SN_S6",
                        "SN_S7",
                    }
                },
            };

        public class Configuration
        {
            public string LogFilePath { get; set; } = @"Monitor.log";

            public string KustoClusterUrl { get; set; } = "https://cbuild.kusto.windows.net";

            public string KustoIngestionClusterUrl { get; set; } = "https://cbuild.kusto.windows.net";

            public string Authority { get; set; } = "72f988bf-86f1-41af-91ab-2d7cd011db47";

            public string ApplicationClientId { get; set; } = "22cabbbb-1f32-4057-b601-225bab98348d";

            public string ApplicationKey { get; set; } = null;

            public KustoWriter<Notification>.Configuration KustoNotifier { get; set; } = new KustoWriter<Notification>.Configuration()
            {
                KustoDatabaseName = "CloudBuildCBTest",
                KustoTableName = "BuildXLCacheMonitor",
                KustoTableIngestionMappingName = "MonitorIngestionMapping",
            };

            public Scheduler.Configuration Scheduler { get; set; } = new Scheduler.Configuration() {
                PersistStatePath = @"SchedulerState.json",
                PersistClearFailedEntriesOnLoad = true,
                MaximumConcurrency = 5,
            };

            /// <summary>
            /// The scheduler writes out logs to the logger, but also writes information about rules that were run
            /// into Kusto. This is used to know when an event has passed (i.e. if a rule has been run again since
            /// the last check and didn't produce a notification, that means the event has passed).
            /// </summary>
            public KustoWriter<Scheduler.LogEntry>.Configuration SchedulerKustoNotifier { get; set; } = new KustoWriter<Scheduler.LogEntry>.Configuration()
            {
                KustoDatabaseName = "CloudBuildCBTest",
                KustoTableName = "BuildXLCacheMonitorSchedulerLog",
                KustoTableIngestionMappingName = "MonitorIngestionMapping",
            };
        }

        private readonly Configuration _configuration;

        private readonly IClock _clock = SystemClock.Instance;
        private readonly Logger _logger;

        private readonly Scheduler _scheduler;
        private readonly KustoWriter<Notification> _alertNotifier;
        private readonly KustoWriter<Scheduler.LogEntry> _schedulerLogWriter;

        private readonly IKustoIngestClient _kustoIngestClient;
        private readonly ICslQueryProvider _cslQueryProvider;

        public Monitor(Configuration configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;

            _logger = CreateLogger();

            // TODO(jubayard): use streaming ingestion instead of direct ingestion. There seems to be some assembly
            // issues when attempting to do that
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
            _schedulerLogWriter = new KustoWriter<Scheduler.LogEntry>(_configuration.SchedulerKustoNotifier, _logger, _kustoIngestClient);

            _scheduler = new Scheduler(_configuration.Scheduler, _logger, _clock, _schedulerLogWriter);
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

                logs.Add(new CsvFileLog(logFilePath, new List<ColumnKind>() {
                    ColumnKind.PreciseTimeStamp,
                    ColumnKind.ProcessId,
                    ColumnKind.ThreadId,
                    ColumnKind.LogLevel,
                    ColumnKind.LogLevelFriendly,
                    ColumnKind.Message,
                }));
            }

            // NOTE(jubayard): making sure things get logged to file first
            logs.Add(new ConsoleLog(useShortLayout: false, printSeverity: true));

            return new Logger(logs.ToArray());
        }

        public Task Run(CancellationToken cancellationToken = default)
        {
            CreateSchedule();
            return _scheduler.RunAsync(cancellationToken);
        }

        private class Instantiation
        {
            public IRule Rule { get; set; }

            public TimeSpan PollingPeriod { get; set; }

            public bool ForceRun { get; set; } = false;
        };

        /// <summary>
        /// Creates the schedule of rules that will be run. Also responsible for configuring them.
        /// </summary>
        private void CreateSchedule()
        {
            OncePerStamp(baseConfiguration =>
            {
                var configuration = new LastProducedCheckpointRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation() {
                    Rule = new LastProducedCheckpointRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(30),
                });
            });

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new LastRestoredCheckpointRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation() {
                    Rule = new LastRestoredCheckpointRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(30),
                });
            });

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new CheckpointSizeRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation() {
                    Rule = new CheckpointSizeRule(configuration),
                    PollingPeriod = configuration.AnomalyDetectionHorizon - TimeSpan.FromMinutes(5),
                });
            });

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new ActiveMachinesRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation() {
                    Rule = new ActiveMachinesRule(configuration),
                    PollingPeriod = configuration.AnomalyDetectionHorizon - TimeSpan.FromMinutes(5),
                });
            });

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new EventHubProcessingDelayRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation() {
                    Rule = new EventHubProcessingDelayRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(20),
                });
            });

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new BuildFailuresRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation()
                {
                    Rule = new BuildFailuresRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(15),
                });
            });

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new FireAndForgetExceptionsRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation() {
                    Rule = new FireAndForgetExceptionsRule(configuration),
                    PollingPeriod = configuration.LookbackPeriod - TimeSpan.FromMinutes(5),
                });
            });

            //OncePerStamp(baseConfiguration =>
            //{
            //    var configuration = new ContractViolationsRule.Configuration(baseConfiguration);
            //    return Utilities.Yield(new Instantiation() {
            //        Rule = new ContractViolationsRule(configuration),
            //        PollingPeriod = configuration.LookbackPeriod,
            //    });
            //});

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
                return failureChecks.Select(check => {
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
            });

            var performanceChecks = new List<OperationPerformanceOutliersRule.DynamicCheck>() {
                new OperationPerformanceOutliersRule.DynamicCheck()
                {
                    LookbackPeriod = TimeSpan.FromMinutes(60),
                    DetectionPeriod = TimeSpan.FromMinutes(30),
                    Match = "LocalContentServer.StartupAsync",
                    Constraint = $"TimeMs >= {TimeSpan.FromMinutes(1).TotalMilliseconds}",
                },
                new OperationPerformanceOutliersRule.DynamicCheck()
                {
                    LookbackPeriod = TimeSpan.FromMinutes(60),
                    DetectionPeriod = TimeSpan.FromMinutes(30),
                    Match = "LocalCacheServer.StartupAsync",
                    Constraint = $"TimeMs >= {TimeSpan.FromMinutes(1).TotalMilliseconds}",
                },
                new OperationPerformanceOutliersRule.DynamicCheck()
                {
                    LookbackPeriod = TimeSpan.FromMinutes(60),
                    DetectionPeriod = TimeSpan.FromMinutes(30),
                    Match = "RedisGlobalStore.RegisterLocalLocationAsync",
                    Constraint = $"TimeMs >= {TimeSpan.FromSeconds(30).TotalMilliseconds}",
                },
                new OperationPerformanceOutliersRule.DynamicCheck()
                {
                    LookbackPeriod = TimeSpan.FromDays(1),
                    DetectionPeriod = TimeSpan.FromHours(1),
                    Match = "CheckpointManager.CreateCheckpointAsync",
                    Constraint = $"TimeMs >= {TimeSpan.FromMinutes(1).TotalMilliseconds}",
                },
                new OperationPerformanceOutliersRule.DynamicCheck()
                {
                    LookbackPeriod = TimeSpan.FromDays(1),
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
            });

            OncePerStamp(baseConfiguration =>
            {
                var configuration = new ServiceRestartsRule.Configuration(baseConfiguration);
                return Utilities.Yield(new Instantiation()
                {
                    Rule = new ServiceRestartsRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(30),
                });
            });
        }

        /// <summary>
        /// Schedules a rule to be run over different stamps and environments.
        /// </summary>
        private void OncePerStamp(Func<KustoRuleConfiguration, IEnumerable<Instantiation>> generator)
        {
            Contract.RequiresNotNull(generator);

            foreach (var kvp in Stamps)
            {
                var environment = kvp.Key;
                foreach (var stamp in kvp.Value)
                {
                    var configuration = new KustoRuleConfiguration()
                    {
                        Clock = _clock,
                        Logger = _logger,
                        Notifier = _alertNotifier,
                        CslQueryProvider = _cslQueryProvider,
                        KustoDatabaseName = EnvironmentToKustoDatabaseName[environment],
                        Environment = environment,
                        Stamp = stamp,
                    };

                    foreach (var entry in generator(configuration))
                    {
                        _scheduler.Add(entry.Rule, entry.PollingPeriod, entry.ForceRun);
                    }
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
                }

                _disposedValue = true;
            }
        }

        public void Dispose() => Dispose(true);
        #endregion


    }
}
