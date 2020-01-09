using System;
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

        public abstract Task Run(RuleContext context);

        protected void Emit(RuleContext context, string bucket, Severity severity, string message, string summary = null, DateTime? eventTimeUtc = null)
        {
            Contract.RequiresNotNull(context);
            Contract.RequiresNotNull(bucket);
            Contract.RequiresNotNull(message);

            var now = _configuration.Clock.UtcNow;
            _configuration.Notifier.Emit(new Notification(
                Identifier,
                context.RunGuid,
                context.RunTimeUtc,
                now,
                eventTimeUtc ?? now,
                bucket,
                severity,
                _configuration.Environment,
                _configuration.Stamp,
                message,
                summary ?? message));
        }

        protected async Task<ObjectReader<T>> QuerySingleResultSetAsync<T>(RuleContext context, string query, string database = null, ClientRequestProperties requestProperties = null)
        {
            Contract.RequiresNotNull(context);
            Contract.RequiresNotNullOrEmpty(query);

            if (database == null)
            {
                database = _configuration.KustoDatabaseName;
            }

            if (requestProperties == null)
            {
                requestProperties = new ClientRequestProperties();
            }
            requestProperties.ClientRequestId = context.RunGuid.ToString();

            var dataReader = await _configuration.CslQueryProvider.ExecuteQueryAsync(database, query, requestProperties);
            return new ObjectReader<T>(dataReader, disposeReader: true, nameBasedColumnMapping: true);
        }
    }
}
