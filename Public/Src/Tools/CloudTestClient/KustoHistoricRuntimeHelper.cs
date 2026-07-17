// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;

namespace Tool.CloudTestClient
{
    /// <summary>
    /// Production implementation of <see cref="HistoricRuntimeHelper"/> that queries
    /// the CloudTest Kusto cluster for historical job execution data. The database queried
    /// is selected based on the target <see cref="CloudTestEnvironment"/>.
    /// </summary>
    internal sealed class KustoHistoricRuntimeHelper : HistoricRuntimeHelper
    {
        private const string KustoClusterUrl = "https://ctest.southcentralus.kusto.windows.net";

        private readonly string m_database;
        private readonly KustoConnectionStringBuilder m_kustoStringBuilder;
        private readonly bool m_debug;
        private readonly Action<string> m_log;

        /// <summary>
        /// Returns the Kusto database that holds historical job execution data for the given environment.
        /// </summary>
        private static string GetDatabase(CloudTestEnvironment environment) => environment switch
        {
            CloudTestEnvironment.Prod => "CloudTestProd",
            CloudTestEnvironment.PPE => "CloudTestPPE",
            CloudTestEnvironment.Dev => "CloudTestDev",
            _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, $"Unknown CloudTest environment '{environment}'."),
        };

        /// <summary>
        /// Creates a new instance using Azure Pipelines workload identity federation (ADO case).
        /// </summary>
        public KustoHistoricRuntimeHelper(string tenantId, string clientId, string serviceConnectionId, string systemAccessToken, CloudTestEnvironment environment, bool debug, Action<string> log = null)
        {
            m_database = GetDatabase(environment);
            var credential = new AzurePipelinesCredential(tenantId, clientId, serviceConnectionId, systemAccessToken);
            m_kustoStringBuilder = new KustoConnectionStringBuilder(KustoClusterUrl, m_database).WithAadAzureTokenCredentialsAuthentication(credential);
            m_debug = debug;
            m_log = log;
        }

        /// <summary>
        /// Creates a new instance using a pre-acquired Entra token authorized to read the CloudTest database (non-ADO case).
        /// </summary>
        public KustoHistoricRuntimeHelper(string entraToken, CloudTestEnvironment environment, bool debug, Action<string> log = null)
        {
            m_database = GetDatabase(environment);
            m_kustoStringBuilder = new KustoConnectionStringBuilder(KustoClusterUrl, m_database).WithAadUserTokenAuthentication(entraToken);
            m_debug = debug;
            m_log = log;
        }

        /// <inheritdoc />
        public override async Task<Dictionary<string, long>> QueryRuntimesAsync(List<string> jobIds, CancellationToken cancellationToken)
        {
            var results = new Dictionary<string, long>();
            using var queryProvider = KustoClientFactory.CreateCslQueryProvider(m_kustoStringBuilder);

            foreach (var batch in Batch(jobIds, DefaultBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string jobIdList = string.Join(", ", batch.Select(id => $"\"{id}\""));
                // Let's match job IDs case-insensitively so we are more robust to any potential case mismatches between the config and Kusto.
                string query = $@"
                    JobExecution
                    | where TIMESTAMP > ago({DefaultLookbackDays}d)
                    | where JobId in~ ({jobIdList})
                    | summarize AvgDurationMs = avg(DurationMs) by JobId";

                if (m_debug)
                {
                    m_log?.Invoke($"DEBUG: Database {m_database}. Kusto query:\n{query}");
                }

                using var reader = await queryProvider.ExecuteQueryAsync(m_database, query, new ClientRequestProperties(), cancellationToken);

                while (reader.Read())
                {
                    // Job IDs are lowercase GUIDs on the config side (Guid.ToString), so normalize the
                    // Kusto-returned ID to lowercase to guarantee it matches the config key.
                    string jobId = reader.GetString(0).ToLowerInvariant();
                    long avgDuration = (long)reader.GetDouble(1);
                    results[jobId] = avgDuration;
                }
            }

            return results;
        }
    }
}
