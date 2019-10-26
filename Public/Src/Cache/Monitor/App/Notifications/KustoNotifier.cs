using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Utils;
using Kusto.Data.Common;
using Kusto.Data.Exceptions;
using Kusto.Ingest;
using Newtonsoft.Json;

namespace BuildXL.Cache.Monitor.App.Notifications
{
    internal class KustoNotifier : INotifier
    {
        public class Settings
        {
            public string KustoDatabaseName { get; set; }

            public string KustoTableName { get; set; }

            public string KustoTableIngestionMappingName { get; set; }

            public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

            public int BatchSize { get; set; } = 1000;

            public bool Validate()
            {
                if (string.IsNullOrEmpty(KustoDatabaseName) || string.IsNullOrEmpty(KustoTableName))
                {
                    return false;
                }

                if (BatchSize <= 0)
                {
                    return false;
                }

                return true;
            }
        }

        private static readonly List<Tuple<string, string>> KustoTableColumns = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("PreciseTimeStamp", "System.DateTime"),
                new Tuple<string, string>("Severity", "System.Int32"),
                new Tuple<string, string>("SeverityFriendly", "System.String"),
                new Tuple<string, string>("Environment", "System.String"),
                new Tuple<string, string>("Stamp", "System.String"),
                new Tuple<string, string>("Message", "System.String"),
            };

        private static readonly List<JsonColumnMapping> KustoJsonMapping = new List<JsonColumnMapping>()
            {
                new JsonColumnMapping()
                {
                    ColumnName = "PreciseTimeStamp",
                    JsonPath = "$.PreciseTimeStamp",
                },
                new JsonColumnMapping()
                {
                    ColumnName = "Severity",
                    JsonPath = "$.Severity",
                },
                new JsonColumnMapping()
                {
                    ColumnName = "SeverityFriendly",
                    JsonPath = "$.SeverityFriendly",
                },
                new JsonColumnMapping()
                {
                    ColumnName = "Environment",
                    JsonPath = "$.Environment",
                },
                new JsonColumnMapping()
                {
                    ColumnName = "Stamp",
                    JsonPath = "$.Stamp",
                },
                new JsonColumnMapping()
                {
                    ColumnName = "Message",
                    JsonPath = "$.Message",
                },
            };

        private readonly ILogger _logger;
        private readonly Settings _settings;
        private readonly IKustoIngestClient _kustoIngestClient;

        private readonly KustoIngestionProperties _kustoIngestionProperties;

        private readonly NagleQueue<Notification> _queue;

        public KustoNotifier(Settings settings, ILogger logger, IKustoIngestClient kustoIngestClient)
        {
            Contract.RequiresNotNull(settings);
            Contract.RequiresNotNull(logger);
            Contract.RequiresNotNull(kustoIngestClient);
            Contract.Requires(settings.Validate());

            _settings = settings;
            _logger = logger;
            _kustoIngestClient = kustoIngestClient;

            _kustoIngestionProperties = new KustoIngestionProperties(_settings.KustoDatabaseName, _settings.KustoTableName)
            {
                Format = DataSourceFormat.json,
            };

            if (string.IsNullOrEmpty(_settings.KustoTableIngestionMappingName))
            {
                _kustoIngestionProperties.JsonMapping = KustoJsonMapping;
            }
            else
            {
                _kustoIngestionProperties.JSONMappingReference = _settings.KustoTableIngestionMappingName;
            }

            _queue = NagleQueue<Notification>.Create(FlushAsync, 1, settings.FlushInterval, settings.BatchSize);
        }

        public void PrepareForIngestion(ICslAdminProvider kustoAdminClient)
        {
            try
            {
                _logger.Debug($"Creating Kusto table named `{_settings.KustoTableName}` on database `{_settings.KustoDatabaseName}`");
                var command = CslCommandGenerator.GenerateTableCreateCommand(_settings.KustoTableName, KustoTableColumns);
                kustoAdminClient.ExecuteControlCommand(databaseName: _settings.KustoDatabaseName, command: command);
            }
            catch (KustoBadRequestException exception)
            {
                // Happens when the DB has already been prepared for execution
                // TODO(jubayard): type should match Kusto.Common.Svc.Exceptions.EntityAlreadyExistsException, but can't find the field in the exception
                if (!exception.Message.Contains("already exists"))
                {
                    throw;
                }
            }

            try
            {
                _logger.Debug($"Creating Kusto table/json mapping named `{_settings.KustoTableIngestionMappingName}` for table `{_settings.KustoTableName}` on database `{_settings.KustoDatabaseName}`");
                var command = CslCommandGenerator.GenerateTableJsonMappingCreateCommand(_settings.KustoTableName, _settings.KustoTableIngestionMappingName, KustoJsonMapping, removeOldestIfRequired: true);
                kustoAdminClient.ExecuteControlCommand(databaseName: _settings.KustoDatabaseName, command: command);
            }
            catch (KustoBadRequestException exception)
            {
                // Happens when the DB has already been prepared for execution
                // TODO: type should match Kusto.Common.Svc.Exceptions.EntityAlreadyExistsException, but it doesn't happen in practice...
                if (!exception.Message.Contains("already exists"))
                {
                    throw;
                }
            }

            _logger.Debug("Kusto is ready for ingestion");
        }

        public void Emit(Notification notification)
        {
            Contract.RequiresNotNull(notification);

            _queue.Enqueue(notification);
        }

        private async Task FlushAsync(IReadOnlyList<Notification> notifications)
        {
            Contract.Assert(notifications.Count > 0);

            try
            {
                _logger.Debug($"Ingesting {notifications.Count} notifications into Kusto");
                var statuses = await KustoIngestAsync(notifications);

                var statistics = statuses.GroupBy(status => status.Status).ToDictionary(kvp => kvp.Key, kvp => kvp.Count());
                var statisticsLine = string.Join(", ", statistics.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                var severity = Severity.Debug;
                if (!statistics.TryGetValue(Status.Succeeded, out var succeeded) || succeeded < notifications.Count)
                {
                    severity = Severity.Error;
                }

                _logger.Log(severity, $"Ingested {notifications.Count} with disaggregation: {statisticsLine}");
            }
            catch (Exception exception)
            {
                // TODO(jubayard): we probably want to do something different here?
                _logger.Error($"Failed to ingest into Kusto: {exception}");
            }
        }

        private async Task<IEnumerable<IngestionStatus>> KustoIngestAsync(IReadOnlyList<Notification> notifications)
        {
            Contract.Assert(notifications.Count > 0);

            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, encoding: Encoding.UTF8);

            foreach (var notification in notifications)
            {
                await writer.WriteLineAsync(JsonConvert.SerializeObject(notification));
            }

            await writer.FlushAsync();
            stream.Seek(0, SeekOrigin.Begin);

            var ingestion = await _kustoIngestClient.IngestFromStreamAsync(stream, _kustoIngestionProperties, new StreamSourceOptions()
            {
                LeaveOpen = true,
            });

            return ingestion.GetIngestionStatusCollection();
        }
    }
}
