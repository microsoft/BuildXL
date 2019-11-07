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
using Kusto.Ingest;
using Newtonsoft.Json;

namespace BuildXL.Cache.Monitor.App.Notifications
{
    internal class KustoWriter<T> : IDisposable, INotifier<T>
    {
        public class Configuration
        {
            public string KustoDatabaseName { get; set; }

            public string KustoTableName { get; set; }

            public string KustoTableIngestionMappingName { get; set; } = null;

            public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

            public int BatchSize { get; set; } = 1000;

            public int MaxDegreeOfParallelism { get; set; } = 5;
        }

        private readonly ILogger _logger;
        private readonly Configuration _configuration;
        private readonly IKustoIngestClient _kustoIngestClient;

        private readonly KustoIngestionProperties _kustoIngestionProperties;

        private readonly NagleQueue<T> _queue;

        public KustoWriter(Configuration configuration, ILogger logger, IKustoIngestClient kustoIngestClient)
        {
            Contract.RequiresNotNull(configuration);
            Contract.RequiresNotNull(logger);
            Contract.RequiresNotNull(kustoIngestClient);

            _configuration = configuration;
            _logger = logger;
            _kustoIngestClient = kustoIngestClient;

            _kustoIngestionProperties = new KustoIngestionProperties(_configuration.KustoDatabaseName, _configuration.KustoTableName)
            {
                Format = DataSourceFormat.json,
            };

            if (!string.IsNullOrEmpty(_configuration.KustoTableIngestionMappingName))
            {
                _kustoIngestionProperties.JSONMappingReference = _configuration.KustoTableIngestionMappingName;
            }

            _queue = NagleQueue<T>.Create(FlushAsync,
                _configuration.MaxDegreeOfParallelism,
                _configuration.FlushInterval,
                _configuration.BatchSize);
        }

        public void Emit(T row)
        {
            _queue.Enqueue(row);
        }

        private async Task FlushAsync(IReadOnlyList<T> rows)
        {
            Contract.Assert(rows.Count > 0);

            try
            {
                _logger.Debug($"Ingesting {rows.Count} notifications into Kusto");
                var statuses = await KustoIngestAsync(rows);

                var statistics = statuses.GroupBy(status => status.Status).ToDictionary(kvp => kvp.Key, kvp => kvp.Count());
                var statisticsLine = string.Join(", ", statistics.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                var severity = Severity.Debug;
                if (statistics.TryGetValue(Status.Failed, out var failed) && failed > 0)
                {
                    severity = Severity.Error;
                }

                _logger.Log(severity, $"Ingested {rows.Count} with disaggregation: {statisticsLine}");
            }
            catch (Exception exception)
            {
                // TODO(jubayard): we probably want to do something different here?
                _logger.Error($"Failed to ingest into Kusto: {exception}");
            }
        }

        private async Task<IEnumerable<IngestionStatus>> KustoIngestAsync(IReadOnlyList<T> rows)
        {
            Contract.Requires(rows.Count > 0);

            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, encoding: Encoding.UTF8);

            foreach (var row in rows)
            {
                await writer.WriteLineAsync(JsonConvert.SerializeObject(row));
            }

            await writer.FlushAsync();
            stream.Seek(0, SeekOrigin.Begin);

            var ingestion = await _kustoIngestClient.IngestFromStreamAsync(stream, _kustoIngestionProperties, new StreamSourceOptions()
            {
                LeaveOpen = true,
            });

            return ingestion.GetIngestionStatusCollection();
        }

        #region IDisposable Support
        private bool _disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _queue.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose() => Dispose(true);
        #endregion
    }
}
