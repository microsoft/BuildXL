// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.App.Rules;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;
using BuildXL.Cache.Monitor.App.Rules.Kusto;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.Scheduling;
using Kusto.Data.Common;
using Kusto.Ingest;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Monitor;
using Microsoft.Azure.Management.Redis.Fluent;

namespace BuildXL.Cache.Monitor.App
{
    public class Monitor : IDisposable
    {
        public class Configuration
        {
            public string KustoClusterUrl { get; set; } = Constants.DefaultKustoClusterUrl;

            public string KustoIngestionClusterUrl { get; set; } = Constants.DefaultKustoClusterUrl;

            public string AzureTenantId { get; set; } = Constants.DefaultAzureTenantId;

            public string AzureAppId { get; set; } = Constants.DefaultAzureAppId;

            public string AzureAppKey { get; set; } = string.Empty;

            /// <summary>
            /// Which environments to monitor
            /// </summary>
            public Dictionary<CloudBuildEnvironment, EnvironmentConfiguration> Environments { get; set; } = Constants.DefaultEnvironments.ToDictionary(x => x.Key, x => x.Value);

            public KustoWriter<Notification>.Configuration KustoNotifier { get; set; } = new KustoWriter<Notification>.Configuration
            {
                KustoDatabaseName = "CloudBuildCBTest",
                KustoTableName = "BuildXLCacheMonitor",
                KustoTableIngestionMappingName = "MonitorIngestionMapping",
            };

            public RuleScheduler.Configuration Scheduler { get; set; } = new RuleScheduler.Configuration
            {
                PersistStatePath = @"SchedulerState.json",
                PersistClearFailedEntriesOnLoad = true,
                MaximumConcurrency = new Dictionary<string, int>() {
                    // Kusto rules always perform some amount of Kusto queries. If we run too many of them at once,
                    // we'll overload the cluster and make queries fail.
                    { "Kusto", 10 },
                    // The autoscaling rules may take arbitrarily long to run, so we keep them on a separate bucket.
                    { "RedisAutoscaler", int.MaxValue },
                },
            };

            /// <summary>
            /// The scheduler writes out logs to the logger, but also writes information about rules that were run
            /// into Kusto. This is used to know when an event has passed (i.e. if a rule has been run again since
            /// the last check and didn't produce a notification, that means the event has passed).
            /// </summary>
            public KustoWriter<RuleScheduler.LogEntry>.Configuration SchedulerKustoNotifier { get; set; } = new KustoWriter<RuleScheduler.LogEntry>.Configuration
            {
                KustoDatabaseName = "CloudBuildCBTest",
                KustoTableName = "BuildXLCacheMonitorSchedulerLog",
                KustoTableIngestionMappingName = "MonitorIngestionMapping",
            };
        }

        private readonly Configuration _configuration;

        private readonly IClock _clock;

        private readonly ILogger _logger;

        private readonly RuleScheduler _scheduler;
        private readonly KustoWriter<Notification> _alertNotifier;
        private readonly KustoWriter<RuleScheduler.LogEntry> _schedulerLogWriter;

        private readonly IReadOnlyDictionary<CloudBuildEnvironment, EnvironmentResources> _environmentResources;

        private readonly IKustoIngestClient _kustoIngestClient;
        private readonly ICslQueryProvider _cslQueryProvider;

        #region Initialization
        public static async Task<Result<Monitor>> CreateAsync(Configuration configuration, ILogger logger)
        {
            logger.Info("Creating Kusto ingest client");
            var kustoIngestClient = ExternalDependenciesFactory.CreateKustoIngestClient(
                configuration.KustoIngestionClusterUrl,
                configuration.AzureTenantId,
                configuration.AzureAppId,
                configuration.AzureAppKey).ThrowIfFailure();

            logger.Info("Creating Kusto query client");
            var cslQueryProvider = ExternalDependenciesFactory.CreateKustoQueryClient(
                configuration.KustoClusterUrl,
                configuration.AzureTenantId,
                configuration.AzureAppId,
                configuration.AzureAppKey).ThrowIfFailure();

            var environmentResources = new Dictionary<CloudBuildEnvironment, EnvironmentResources>();

            // This does a bunch of Azure API calls, which are really slow. Making them a bit faster by doing them
            // concurrently.
            await configuration.Environments.ParallelForEachAsync(async (keyValuePair) =>
            {
                var environment = keyValuePair.Key;
                var environmentConfiguration = keyValuePair.Value;

                logger.Info($"Loading resources for environment `{environment}`");
                var resources = await CreateEnvironmentResourcesAsync(configuration, environmentConfiguration);

                lock (environmentResources)
                {
                    environmentResources[environment] = resources;
                }
            });

            return new Monitor(configuration, kustoIngestClient, cslQueryProvider, SystemClock.Instance, environmentResources, logger);
        }

        private static async Task<EnvironmentResources> CreateEnvironmentResourcesAsync(Configuration configuration, EnvironmentConfiguration environmentConfiguration)
        {
            var azure = ExternalDependenciesFactory.CreateAzureClient(
                configuration.AzureTenantId,
                environmentConfiguration.AzureSubscriptionId,
                configuration.AzureAppId,
                configuration.AzureAppKey).ThrowIfFailure();

            var monitorManagementClient = await ExternalDependenciesFactory.CreateAzureMetricsClientAsync(
                configuration.AzureTenantId,
                environmentConfiguration.AzureSubscriptionId,
                configuration.AzureAppId,
                configuration.AzureAppKey).ThrowIfFailureAsync();

            var redisCaches = (await azure.RedisCaches.ListAsync()).ToDictionary(cache => cache.Name, cache => cache);

            return new EnvironmentResources(azure, monitorManagementClient, redisCaches);
        }

        private Monitor(Configuration configuration, IKustoIngestClient kustoIngestClient, ICslQueryProvider cslQueryProvider, IClock clock, IReadOnlyDictionary<CloudBuildEnvironment, EnvironmentResources> environmentResources, ILogger logger)
        {
            _configuration = configuration;

            _clock = clock;
            _logger = logger;
            _kustoIngestClient = kustoIngestClient;
            _cslQueryProvider = cslQueryProvider;
            _environmentResources = environmentResources;

            _alertNotifier = new KustoWriter<Notification>(_configuration.KustoNotifier, _logger, _kustoIngestClient);

            _schedulerLogWriter = new KustoWriter<RuleScheduler.LogEntry>(_configuration.SchedulerKustoNotifier, _logger, _kustoIngestClient);

            _scheduler = new RuleScheduler(_configuration.Scheduler, _logger, _clock, _schedulerLogWriter);
        }

        private class EnvironmentResources
        {
            public IAzure Azure { get; }

            public IMonitorManagementClient MonitorManagementClient { get; }

            public IReadOnlyDictionary<string, IRedisCache> RedisCaches { get; }

            public EnvironmentResources(IAzure azure, IMonitorManagementClient monitorManagementClient, IReadOnlyDictionary<string, IRedisCache> redisCaches)
            {
                Azure = azure;
                MonitorManagementClient = monitorManagementClient;
                RedisCaches = redisCaches;
            }
        }
        #endregion

        public async Task RunAsync(Action? onWatchlistChange = null, CancellationToken cancellationToken = default)
        {
            _logger.Info("Loading stamps to watch");

            // The watchlist is essentially immutable on every run. The reason is that a change to the Watchlist
            // likely triggers a change in the scheduled rules, so we need to regenerate the entire schedule.
            var watchlist = await Watchlist.CreateAsync(_logger, _cslQueryProvider, _configuration.Environments);

            _logger.Info("Creating rules schedule");
            CreateSchedule(watchlist);

            if (onWatchlistChange != null)
            {
                // This rule takes care of updating the watchlist and triggering the action when it has effectively
                // changed. The action will take care of restarting the entire application.
                _scheduler.Add(new LambdaRule(
                    identifier: "WatchlistUpdate",
                    concurrencyBucket: "Kusto",
                    lambda: async (context) =>
                    {
                        if (await watchlist.RefreshAsync())
                        {
                            onWatchlistChange?.Invoke();
                        }
                    }), TimeSpan.FromMinutes(30));
            }

            _logger.Info("Entering scheduler loop");
            await _scheduler.RunAsync(cancellationToken);
        }

        #region Schedule Generation
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
            OncePerStamp(arguments =>
            {
                var configuration = new LastProducedCheckpointRule.Configuration(arguments.BaseConfiguration);
                return Analysis.Utilities.Yield(new Instantiation()
                {
                    Rule = new LastProducedCheckpointRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(40),
                });
            }, watchlist);

            OncePerStamp(arguments =>
            {
                var configuration = new LastRestoredCheckpointRule.Configuration(arguments.BaseConfiguration);
                return Analysis.Utilities.Yield(new Instantiation()
                {
                    Rule = new LastRestoredCheckpointRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(30),
                });
            }, watchlist);

            OncePerStamp(arguments =>
            {
                var configuration = new CheckpointSizeRule.Configuration(arguments.BaseConfiguration);
                return Analysis.Utilities.Yield(new Instantiation()
                {
                    Rule = new CheckpointSizeRule(configuration),
                    PollingPeriod = configuration.AnomalyDetectionHorizon - TimeSpan.FromMinutes(5),
                });
            }, watchlist);

            // TODO: this rule is too noisy and inaccurate, we should make it work again
            //OncePerStamp(arguments =>
            //{
            //    var configuration = new ActiveMachinesRule.Configuration(arguments.BaseConfiguration);
            //    return Analysis.Utilities.Yield(new Instantiation()
            //    {
            //        Rule = new ActiveMachinesRule(configuration),
            //        PollingPeriod = configuration.AnomalyDetectionHorizon - TimeSpan.FromMinutes(5),
            //    });
            //}, watchlist);

            OncePerStamp(arguments =>
            {
                var configuration = new EventHubProcessingDelayRule.Configuration(arguments.BaseConfiguration);
                return Analysis.Utilities.Yield(new Instantiation()
                {
                    Rule = new EventHubProcessingDelayRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(30),
                });
            }, watchlist);

            OncePerStamp(arguments =>
            {
                var configuration = new BuildFailuresRule.Configuration(arguments.BaseConfiguration);
                return Analysis.Utilities.Yield(new Instantiation()
                {
                    Rule = new BuildFailuresRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(45),
                });
            }, watchlist);

            // TODO: fire-and-forget exceptions are now being reported on the dashboards. We should see if this can be recycled.
            //OncePerStamp(arguments =>
            //{
            //    var configuration = new FireAndForgetExceptionsRule.Configuration(arguments.BaseConfiguration);
            //    return Analysis.Utilities.Yield(new Instantiation()
            //    {
            //        Rule = new FireAndForgetExceptionsRule(configuration),
            //        PollingPeriod = configuration.LookbackPeriod - TimeSpan.FromMinutes(5),
            //    });
            //}, watchlist);

            // TODO: this was just too noisy
            //OncePerStamp(arguments =>
            //{
            //    var configuration = new ContractViolationsRule.Configuration(arguments.BaseConfiguration);
            //    return Analysis.Utilities.Yield(new Instantiation() {
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

            OncePerStamp(arguments =>
            {
                return failureChecks.Select(check =>
                {
                    var configuration = new OperationFailureCheckRule.Configuration(arguments.BaseConfiguration, check);

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

            OncePerStamp(arguments =>
            {
                return performanceChecks.Select(check =>
                {
                    var configuration = new OperationPerformanceOutliersRule.Configuration(arguments.BaseConfiguration, check);

                    return new Instantiation
                    {
                        Rule = new OperationPerformanceOutliersRule(configuration),
                        PollingPeriod = check.DetectionPeriod - TimeSpan.FromMinutes(5),
                    };
                });
            }, watchlist);

            OncePerStamp(arguments =>
            {
                var configuration = new ServiceRestartsRule.Configuration(arguments.BaseConfiguration);
                return Analysis.Utilities.Yield(new Instantiation()
                {
                    Rule = new ServiceRestartsRule(configuration),
                    PollingPeriod = TimeSpan.FromMinutes(30),
                });
            }, watchlist);

            OncePerStamp(GenerateRedisAutoscalingRules, watchlist);
        }

        private IEnumerable<Instantiation> GenerateRedisAutoscalingRules(RuleGeneratorArguments arguments)
        {
            if (!arguments.DynamicStampProperties.RedisAutoscalingEnabled)
            {
                yield break;
            }

            var autoscalingAgentConfiguration = new RedisAutoscalingAgent.Configuration();
            if (arguments.DynamicStampProperties.RedisAutoscalingMaximumClusterMemoryAllowedMb > 0)
            {
                autoscalingAgentConfiguration.MaximumClusterMemoryAllowedMb = arguments.DynamicStampProperties.RedisAutoscalingMaximumClusterMemoryAllowedMb;
            }

            var redisAutoscalingAgent = new RedisAutoscalingAgent(autoscalingAgentConfiguration, arguments.EnvironmentResources.MonitorManagementClient);
            var configuration = new RedisAutoscalingRule.Configuration(arguments.BaseConfiguration);

            var primaryRedisInstance = RedisInstance
                .FromPreloaded(arguments.EnvironmentResources.Azure, arguments.EnvironmentResources.RedisCaches[arguments.StampId.PrimaryRedisName])
                .ThrowIfFailure();

            var secondaryRedisInstance = RedisInstance
                .FromPreloaded(arguments.EnvironmentResources.Azure, arguments.EnvironmentResources.RedisCaches[arguments.StampId.SecondaryRedisName])
                .ThrowIfFailure();

            yield return new Instantiation
            {
                Rule = new RedisAutoscalingRule(configuration, redisAutoscalingAgent, primaryRedisInstance, secondaryRedisInstance),
                PollingPeriod = TimeSpan.FromMinutes(10),
            };
        }

        /// <summary>
        /// Schedules a rule to be run over different stamps and environments.
        /// </summary>
        private void OncePerStamp(Func<RuleGeneratorArguments, IEnumerable<Instantiation>> generator, Watchlist watchlist)
        {
            foreach (var (stampId, properties) in watchlist.Entries)
            {
                var configuration = new KustoRuleConfiguration(
                    _clock,
                    _logger,
                    _alertNotifier,
                    _cslQueryProvider,
                    stampId,
                    _configuration.Environments[stampId.Environment].KustoDatabaseName,
                    properties.CacheTableName);

                var request = new RuleGeneratorArguments
                {
                    StampId = stampId,
                    DynamicStampProperties = properties,
                    BaseConfiguration = configuration,
                    EnvironmentResources = _environmentResources[stampId.Environment],
                };

                foreach (var rule in generator(request))
                {
                    Contract.AssertNotNull(rule.Rule);
                    _scheduler.Add(rule.Rule, rule.PollingPeriod, rule.ForceRun);
                }
            }
        }

        private class RuleGeneratorArguments
        {
            public StampId StampId { get; set; }

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            public Watchlist.DynamicStampProperties DynamicStampProperties { get; set; }

            public KustoRuleConfiguration BaseConfiguration { get; set; }

            public EnvironmentResources EnvironmentResources { get; set; }
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        }

        private class Instantiation
        {
            public IRule? Rule { get; set; }

            public TimeSpan PollingPeriod { get; set; }

            public bool ForceRun { get; set; } = false;
        };
        #endregion

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
                }

                _disposedValue = true;
            }
        }

        public void Dispose() => Dispose(true);
        #endregion
    }
}
