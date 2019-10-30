using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Notifications;
using Kusto.Cloud.Platform.Data;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal abstract class KustoRuleBase : IRule
    {
        public abstract string Identifier { get; }

        private readonly KustoRuleConfiguration _configuration;

        public KustoRuleBase(KustoRuleConfiguration configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;
        }

        public abstract Task Run();

        protected void Emit(Severity severity, string message, string summary = null, DateTime? ruleRunTimeUtc = null, DateTime? eventTimeUtc = null)
        {
            Contract.RequiresNotNull(message);

            var now = _configuration.Clock.UtcNow;
            _configuration.Notifier.Emit(new Notification(
                Identifier,
                ruleRunTimeUtc ?? now,
                now,
                eventTimeUtc ?? now,
                severity,
                _configuration.Environment,
                _configuration.Stamp,
                message,
                summary ?? message));
        }

        protected async Task<ObjectReader<T>> QuerySingleResultSetAsync<T>(string query)
        {
            var dataReader = await _configuration.CslQueryProvider.ExecuteQueryAsync(
                _configuration.KustoDatabaseName,
                query,
                new ClientRequestProperties()
                {
                    ClientRequestId = Guid.NewGuid().ToString()
                });

            return new ObjectReader<T>(dataReader, disposeReader: true, nameBasedColumnMapping: true);
        }

        protected async Task<List<T>> QuerySingleResultSetAsync<T>(string query, Func<IDataRecord, T> transformer)
        {
            using var dataReader = await _configuration.CslQueryProvider.ExecuteQueryAsync(
                _configuration.KustoDatabaseName,
                query,
                new ClientRequestProperties()
                {
                    ClientRequestId = Guid.NewGuid().ToString()
                });

            var result = new List<T>();
            while (!dataReader.IsClosed && dataReader.Read())
            {
                result.Add(transformer(dataReader));
            }

            return result;
        }
    }
}
