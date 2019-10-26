using System;
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
    internal class Program
    {
        public class Settings
        {
            public string KustoClusterUrl { get; set; } = "https://cbuild.kusto.windows.net";

            public string KustoIngestionClusterUrl { get; set; } = "https://cbuild.kusto.windows.net";

            public string Authority { get; set; } = "72f988bf-86f1-41af-91ab-2d7cd011db47";

            public string ApplicationClientId { get; set; } = "22cabbbb-1f32-4057-b601-225bab98348d";

            public string ApplicationKey { get; set; } = "";

            public KustoNotifier.Settings KustoNotifier { get; set; } = new KustoNotifier.Settings()
            {
                KustoDatabaseName = "CloudBuildCBTest",
                KustoTableName = "BuildXLCacheMonitor",
            };

            public Scheduler.Settings Scheduler { get; set; } = new Scheduler.Settings();

            public bool Validate()
            {
                if (string.IsNullOrEmpty(KustoClusterUrl) || string.IsNullOrEmpty(KustoIngestionClusterUrl) || string.IsNullOrEmpty(Authority) || string.IsNullOrEmpty(ApplicationClientId) || string.IsNullOrEmpty(ApplicationKey))
                {
                    return false;
                }

                if (!KustoNotifier.Validate())
                {
                    return false;
                }

                return true;
            }
        }

        private static void Main(string[] args)
        {
            var settings = LoadSettings();

            var clock = SystemClock.Instance;
            var logger = new Logger(new ILog[] {
                new ConsoleLog(useShortLayout: false, printSeverity: true),
            });

            var kustoIngestConnectionString = new KustoConnectionStringBuilder(settings.KustoIngestionClusterUrl)
                .WithAadApplicationKeyAuthentication(settings.ApplicationClientId, settings.ApplicationKey, settings.Authority);
            // TODO(jubayard): use streaming ingestion instead of direct ingestion. There seems to be some assembly
            // issues when attempting to do that
            using var ingestClient = KustoIngestFactory.CreateDirectIngestClient(kustoIngestConnectionString);

            var notifier = new KustoNotifier(settings.KustoNotifier, logger, ingestClient);

            var kustoConnectionString = new KustoConnectionStringBuilder(settings.KustoClusterUrl)
                .WithAadApplicationKeyAuthentication(settings.ApplicationClientId, settings.ApplicationKey, settings.Authority);

            //using (var adminClient = KustoClientFactory.CreateCslAdminProvider(kustoConnectionString))
            //{
            //    notifier.PrepareForIngestion(adminClient);
            //}

            using var kustoQueryClient = KustoClientFactory.CreateCslQueryProvider(kustoConnectionString);
            var scheduler = new Scheduler(settings.Scheduler, logger, clock);

            AddRules(scheduler, notifier, kustoQueryClient);

            scheduler.RunAsync().Wait();
        }

        private static Settings LoadSettings()
        {
            return new Settings();
        }

        private static void AddRules(Scheduler scheduler, INotifier notifier, ICslQueryProvider cslQueryProvider)
        {
            scheduler.Add(new PrintRule(notifier), TimeSpan.FromSeconds(1));
        }
    }
}
