using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.App.Rules;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;
using Kusto.Ingest;

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
                //{
                //    Env.Production, new [] {
                //        "MW_S8",
                //        "MW_S9",
                //    }
                //},
            };

        public class Configuration
        {
            public string KustoClusterUrl { get; set; } = "https://cbuild.kusto.windows.net";

            public string KustoIngestionClusterUrl { get; set; } = "https://cbuild.kusto.windows.net";

            public string Authority { get; set; } = "72f988bf-86f1-41af-91ab-2d7cd011db47";

            public string ApplicationClientId { get; set; } = "22cabbbb-1f32-4057-b601-225bab98348d";

            public string ApplicationKey { get; set; } = "-mvo2ofU@fm]G8uH6B+hoUACUM7nTRdm";

            public KustoNotifier.Configuration KustoNotifier { get; set; } = new KustoNotifier.Configuration()
            {
                KustoDatabaseName = "CloudBuildCBTest",
                KustoTableName = "BuildXLCacheMonitor",
            };

            public Scheduler.Configuration Scheduler { get; set; } = new Scheduler.Configuration() {
                PersistState = true,
                PersistStatePath = @"C:\work\Scheduler.json",
                PersistClearFailedEntriesOnLoad = true,
            };
        }

        private readonly Configuration _configuration;

        private readonly IClock _clock = SystemClock.Instance;
        private readonly Logger _logger;

        private readonly Scheduler _scheduler;
        private readonly INotifier _notifier;

        private readonly IKustoIngestClient _kustoIngestClient;
        private readonly ICslQueryProvider _cslQueryProvider;

        public Monitor(Configuration configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;

            _logger = new Logger(new ILog[] {
                new ConsoleLog(useShortLayout: false, printSeverity: true),
            });

            // TODO(jubayard): use streaming ingestion instead of direct ingestion. There seems to be some assembly
            // issues when attempting to do that
            var kustoIngestConnectionString = new KustoConnectionStringBuilder(_configuration.KustoIngestionClusterUrl)
                .WithAadApplicationKeyAuthentication(_configuration.ApplicationClientId, _configuration.ApplicationKey, _configuration.Authority);
            _kustoIngestClient = KustoIngestFactory.CreateDirectIngestClient(kustoIngestConnectionString);

            var kustoConnectionString = new KustoConnectionStringBuilder(_configuration.KustoClusterUrl)
                .WithAadApplicationKeyAuthentication(_configuration.ApplicationClientId, _configuration.ApplicationKey, _configuration.Authority);
            _cslQueryProvider = KustoClientFactory.CreateCslQueryProvider(kustoConnectionString);

            _notifier = new KustoNotifier(_configuration.KustoNotifier, _logger, _kustoIngestClient);
            _scheduler = new Scheduler(_configuration.Scheduler, _logger, _clock);
        }

        public Task Run()
        {
            Schedule();
            return _scheduler.RunAsync();
        }

        /// <summary>
        /// Creates the schedule of rules that will be run. Also responsible for configuring them.
        /// </summary>
        private void Schedule()
        {
            Add(kustoConfiguration =>
            {
                return new LastProducedCheckpointRule(new LastProducedCheckpointRule.Configuration(kustoConfiguration));
            }, pollingPeriod: TimeSpan.FromMinutes(30));

            Add(kustoConfiguration =>
            {
                return new LastRestoredCheckpointRule(new LastRestoredCheckpointRule.Configuration(kustoConfiguration));
            }, pollingPeriod: TimeSpan.FromMinutes(30));
        }

        /// <summary>
        /// Schedules a rule to be run over different stamps and environments.
        /// </summary>
        private void Add(Func<KustoRuleConfiguration, IRule> generator, TimeSpan pollingPeriod)
        {
            // TODO(jubayard): this could be done more efficiently by not creating a KustoRuleConfiguration for every
            // call (i.e. first getting the list of rules, and then creating the instances).
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
                        Notifier = _notifier,
                        CslQueryProvider = _cslQueryProvider,
                        KustoDatabaseName = EnvironmentToKustoDatabaseName[environment],
                        Environment = environment,
                        Stamp = stamp,
                    };

                    _scheduler.Add(generator(configuration), pollingPeriod);
                }
            }
        }

        public void Dispose()
        {
            _kustoIngestClient?.Dispose();
            _cslQueryProvider?.Dispose();
            _logger?.Dispose();
        }
    }
}
