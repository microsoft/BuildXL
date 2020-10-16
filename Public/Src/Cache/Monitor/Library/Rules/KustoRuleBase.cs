// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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

        protected Task<IReadOnlyList<T>> QueryKustoAsync<T>(RuleContext context, string query, string? database = null, ClientRequestProperties? requestProperties = null)
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

            return _configuration.KustoClient.QueryAsync<T>(query, database, requestProperties);
        }
    }
}
