// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Az;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.App.Scheduling;
using Kusto.Cloud.Platform.Data;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal abstract class KustoRuleBase : IRule
    {
        public abstract string Identifier { get; }

        public virtual string ConcurrencyBucket { get; } = "Kusto";

        private readonly KustoRuleConfiguration _configuration;

        public KustoRuleBase(KustoRuleConfiguration configuration)
        {
            _configuration = configuration;
        }

        public abstract Task Run(RuleContext context);

        protected void Emit(RuleContext context, string bucket, Severity severity, string message, string? summary = null, DateTime? eventTimeUtc = null)
        {
            var now = _configuration.Clock.UtcNow;
            _configuration.Logger.Log(severity, $"[{Identifier}] {message}");
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

        protected Task<ObjectReader<T>> QueryKustoAsync<T>(RuleContext context, string query, string? database = null, ClientRequestProperties? requestProperties = null)
        {
            if (database == null)
            {
                database = _configuration.KustoDatabaseName;
            }

            if (requestProperties == null)
            {
                requestProperties = new ClientRequestProperties();
            }
            requestProperties.ClientRequestId = context.RunGuid.ToString();

            return _configuration.CslQueryProvider.QuerySingleResultSetAsync<T>(query, database, requestProperties);
        }
    }
}
