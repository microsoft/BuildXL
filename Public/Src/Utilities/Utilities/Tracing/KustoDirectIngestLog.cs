// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Azure.Core;
using Azure.Identity;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Core.Tracing;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Exceptions;
using Kusto.Data.Net.Client;
using Kusto.Ingest;

#nullable enable

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Handles direct ingestion of unstructured telemetry lines into a Kusto cluster.
    /// </summary>
    /// <remarks>
    /// The connection URI encodes the cluster, database, and the Entra identity to authenticate as:
    ///
    /// <b>ADO workload identity federation (recommended for ADO):</b>
    /// <c>https://cluster.region.kusto.windows.net/Database?clientId=&lt;clientId&gt;&amp;serviceConnectionId=&lt;adobConnId&gt;&amp;tenantId=&lt;tid&gt;</c>
    /// The pipeline step must expose <c>$(System.AccessToken)</c> via the <c>SYSTEM_ACCESSTOKEN</c>
    /// environment variable.  Authentication is performed by <see cref="AzurePipelinesCredential"/>.
    ///
    /// <b>Managed identity (fallback for ADO):</b>
    /// <c>https://cluster.region.kusto.windows.net/Database?clientId=&lt;miClientId&gt;</c>
    /// Authenticates via IMDS using <see cref="ManagedIdentityCredential"/>; only works when bxl
    /// runs on a VM that has the specified user-assigned managed identity attached.
    ///
    /// <b>Interactive authentication:</b>
    /// <c>https://cluster.region.kusto.windows.net/Database?tenantId=&lt;tid&gt;</c>
    /// Authenticates via an interactive prompt using <see cref="InteractiveClientTokenCredential"/>, which supports various interactive flows (browser, device code, Visual Studio Code) 
    /// depending on the platform and availability.  This is primarily intended for local development and testing. Interactive authentication needs to be allowed explicitly via /interactive+
    /// 
    /// In all cases the authenticated client must belong to a principal that has been granted the
    /// <c>ingestor</c> role on the target Kusto database.
    ///
    /// On startup the class attempts to create the target table and its ingestion mapping via the
    /// Kusto management endpoint if they do not yet exist.  If the identity only holds the
    /// <c>ingestor</c> role the DDL step is silently skipped and the table must be pre-created
    /// by the customer. Any <c>Database Admin</c> (or higher) role enables automatic creation.
    /// </remarks>
    public sealed class KustoDirectIngestLog : IDisposable
    {
        /// <summary>
        /// Authentication methods for Kusto ingestion.
        /// </summary>
        private enum UserRequestedAuthenticationMethod
        {
            WorkloadIdentityFederation,
            ManagedIdentity,
            InteractiveAuthentication
        }

        private static readonly string s_identityFormat =
            $"{Environment.NewLine}Workload identity format: https://cluster.region.kusto.windows.net/DB?clientId=<cid>&tenantId=<tid>&serviceConnectionId=<connId>" +
            $"{Environment.NewLine}Managed identity format: https://cluster.region.kusto.windows.net/DB?clientId=<cid>" + 
            $"{Environment.NewLine}Interactive authentication format: https://cluster.region.kusto.windows.net/DB?tenantId=<tid>";

        private readonly IKustoQueuedIngestClient m_ingestClient;
        private readonly KustoIngestionProperties m_ingestionProperties;
        private readonly ChannelBatchReader m_batchReader;
        private readonly Action<string> m_errorLogger;
        private readonly Guid m_relatedActivityId;

        private int m_disposed;

        /// <summary>
        /// Parses <paramref name="ingestUri"/>, selects an appropriate credential, and builds a
        /// <see cref="KustoDirectIngestLog"/>
        /// </summary>
        public static KustoDirectIngestLog? TryCreate(
            Guid relatedActivityId,
            string ingestUri,
            bool allowInteractiveAuth,
            Action<string> errorLogger,
            Action<string> debugLogger,
            IConsole console,
            string tableName,
            string mappingName,
            string tableSchema,
            string mappingJson,
            DataSourceFormat dataSourceFormat,
            CancellationToken cancellationToken)
        {
            if (!TryParseIngestUri(ingestUri, out var clusterUrl, out var database, out var clientId, out var tenantId, out var serviceConnectionId, out var parseError))
            {
                errorLogger($"Invalid ingest URI '{ingestUri}': {parseError}. Direct Kusto ingestion is disabled.");
                return null;
            }

            if (!TryDecideAuthMethod(ingestUri, errorLogger, allowInteractiveAuth, clientId, tenantId, serviceConnectionId, out var authMethod))
            {
                // Error is already logged by TryDecideAuthMethod.
                return null;
            }

            try
            {
                TokenCredential credential;
                switch (authMethod)
                {
                    case UserRequestedAuthenticationMethod.WorkloadIdentityFederation:
                        // An Azure pipeline credential needs the system access token. This should be available since 1ESPT exposes
                        // it as part of the BuildXL workflow, but let's check anyway and report otherwise
                        var systemAccessToken = Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN");
                        if (string.IsNullOrEmpty(systemAccessToken))
                        {
                            errorLogger(
                                "SYSTEM_ACCESSTOKEN environment variable is not set. " +
                                "Add 'env: SYSTEM_ACCESSTOKEN: $(System.AccessToken)' to the pipeline step. " +
                                "Direct Kusto ingestion is disabled.");
                            return null;
                        }

                        credential = new AzurePipelinesCredential(tenantId, clientId, serviceConnectionId, systemAccessToken);
                        break;
                    case UserRequestedAuthenticationMethod.ManagedIdentity:
                        // Managed identity: resolve via IMDS on the current VM.
                        credential = new ManagedIdentityCredential(clientId);
                        break;
                    case UserRequestedAuthenticationMethod.InteractiveAuthentication:
                        credential = new InteractiveClientTokenCredential(
                            debugLogger,
                            $"Authenticate to access Kusto cluster for ingestion. Tenant: {tenantId}",
                            tenantId,
                            console: console,
                            cancellationToken: cancellationToken);
                        break;
                    default:
                        errorLogger($"Unsupported authentication method for '{ingestUri}'. {s_identityFormat} Direct Kusto ingestion is disabled.");
                        return null;
                }

                var resolvedTable = tableName;
                var ingestUrl = DeriveIngestUrlFromClusterUrl(clusterUrl);
                var instance = new KustoDirectIngestLog(relatedActivityId, ingestUrl, database, resolvedTable, mappingName, credential, dataSourceFormat, errorLogger);

                // Best-effort: ensure the target table and mapping exist before ingestion starts.
                EnsureTableAndMappingAsync(clusterUrl, database, resolvedTable, tableSchema, mappingJson, mappingName, credential, errorLogger)
                    .GetAwaiter().GetResult();

                return instance;
            }
            catch (Exception ex)
            {
                errorLogger($"Failed to initialise Kusto ingest client: {ex.GetLogEventMessage()}. Direct Kusto ingestion is disabled.");
                return null;
            }
        }

        private KustoDirectIngestLog(
            Guid relatedActivityId,
            string clusterIngestUrl,
            string database,
            string table,
            string mappingName,
            TokenCredential credential,
            DataSourceFormat dataSourceFormat,
            Action<string> errorLogger)
        {
            m_errorLogger = errorLogger;

            var kcsb = new KustoConnectionStringBuilder(clusterIngestUrl)
                .WithAadAzureTokenCredentialsAuthentication(credential);

            m_ingestClient = KustoIngestFactory.CreateQueuedIngestClient(kcsb);

            m_ingestionProperties = new KustoIngestionProperties(database, table)
            {
                Format = dataSourceFormat,
                IngestionMapping = new IngestionMapping { IngestionMappingReference = mappingName },
            };

            m_batchReader = new ChannelBatchReader(FlushBatchAsync, errorLogger: errorLogger);
            m_relatedActivityId = relatedActivityId;
        }

        /// <summary>
        /// Enqueues a single formatted log line for background ingestion.
        /// This method is non-blocking and thread-safe.
        /// </summary>
        public void Write(string line) => m_batchReader.Write(line);

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            try
            {
                // Dispose completes the channel writer, cancels the background loop, and blocks
                // until the final drain is done.
                m_batchReader.Dispose();
            }
            catch (Exception ex)
            {
                m_errorLogger($"Error during shutdown flush: {ex.GetLogEventMessage()}");
            }
            finally
            {
                m_ingestClient.Dispose();
            }
        }

        private async Task FlushBatchAsync(Stream stream, CancellationToken cancellationToken)
        {
            try
            {
                await m_ingestClient.IngestFromStreamAsync(
                    stream,
                    m_ingestionProperties,
                    new StreamSourceOptions { 
                        SourceId = m_relatedActivityId,
                        // Do not dispose the stream after ingestion: it is owned by the ChannelBatchReader
                        // and will be reused for the next batch after this callback returns.
                        LeaveOpen = true 
                    });
            }
            catch (Exception ex)
            {
                m_errorLogger($"Failed to ingest batch: {ex.GetLogEventMessage()}");
            }
        }

        /// <summary>
        /// Best-effort: creates the Kusto table and ingestion mapping when they do not yet exist.
        /// Silently skipped when the identity only holds the <c>ingestor</c> role (authorization
        /// failure); the table must then be pre-created by the customer.  Any other failure is
        /// logged as a non-fatal warning.
        /// </summary>
        private static async Task EnsureTableAndMappingAsync(
            string managementUrl,
            string database,
            string tableName,
            string tableSchema,
            string mappingJson,
            string mappingName,
            TokenCredential credential,
            Action<string> errorLogger)
        {
            try
            {
                var kcsbAdmin = new KustoConnectionStringBuilder(managementUrl)
                    .WithAadAzureTokenCredentialsAuthentication(credential);

                using var adminClient = KustoClientFactory.CreateCslAdminProvider(kcsbAdmin);

                // Probe whether the table exists. TableNotFoundException means it doesn't exist yet;
                // any other exception propagates to the outer catch.
                bool tableAlreadyExisted = true;
                try
                {
                    await adminClient.ExecuteControlCommandAsync(database, $".show table {tableName}");
                }
                catch (EntityNotFoundException)
                {
                    tableAlreadyExisted = false;
                }

                if (!tableAlreadyExisted)
                {
                    // Table didn't exist — create it and apply a 7-day retention policy.
                    // Never touch the policy on a pre-existing table; the customer may have
                    // configured their own retention settings.
                    await adminClient.ExecuteControlCommandAsync(
                        database,
                        $".create-merge table {tableName} {tableSchema}");

                    await adminClient.ExecuteControlCommandAsync(
                        database,
                        $".alter-merge table {tableName} policy retention '{{\"SoftDeletePeriod\": \"7.00:00:00\", \"Recoverability\": \"Disabled\"}}'");
                }

                // The mapping type is always "csv" (ordinal-position); the actual wire
                // format (psv vs csv) is specified separately at ingest time.
                // Always run so that a missing mapping on a pre-existing table is also covered.
                await adminClient.ExecuteControlCommandAsync(
                    database,
                    $".create-or-alter table {tableName} ingestion csv mapping \"{mappingName}\" '{mappingJson}'");
            }
            catch (KustoRequestDeniedException)
            {
                // The identity only has ingestor rights — DDL is not permitted.  This is expected
                // when the customer pre-creates the table themselves; swallow silently.
            }
            catch (Exception ex)
            {
                // Unexpected failure (network, schema conflict, etc.) — log but do not abort.
                errorLogger($"Table/mapping auto-creation failed (non-fatal): {ex.GetLogEventMessage()}");
            }
        }

        /// <summary>
        /// Parses a combined cluster URI + database + identity parameters.
        /// </summary>
        internal static bool TryParseIngestUri(
            string ingestUri,
            out string clusterUrl,
            out string database,
            out string clientId,
            out string tenantId,
            out string serviceConnectionId,
            out string error)
        {
            clusterUrl = string.Empty;
            database = string.Empty;
            clientId = string.Empty;
            tenantId = string.Empty;
            serviceConnectionId = string.Empty;
            error = string.Empty;

            if (!Uri.TryCreate(ingestUri, UriKind.Absolute, out var uri))
            {
                error = "Not a valid absolute URI.";
                return false;
            }

            var segments = uri.Segments;
            if (segments.Length != 2 || string.IsNullOrWhiteSpace(segments[1]))
            {
                error = $"No database name found in path of '{ingestUri}'. " +
                        $"Expected format: {s_identityFormat}";
                return false;
            }

            // Parse query parameters. Pick up the parameters we need, ignore everything else.
            var queryParams = HttpUtility.ParseQueryString(uri.Query.TrimStart('?'));

            if (queryParams["clientId"] is var clientIdParam && clientIdParam is not null)
            {
                clientId = clientIdParam;
            }

            if (queryParams["tenantId"] is var tenantIdParam && tenantIdParam is not null)
            {
                tenantId = tenantIdParam;
            }

            if (queryParams["serviceConnectionId"] is var serviceConnectionIdParam && serviceConnectionIdParam is not null)
            {
                serviceConnectionId = serviceConnectionIdParam;
            }

            database = segments[1].Trim('/');
            clusterUrl = $"{uri.Scheme}://{uri.Host}";
            return true;
        }

        /// <summary>
        /// Derives the Kusto DM (ingest) endpoint URL from the cluster (management) URL by
        /// prepending <c>ingest-</c> to the hostname if needed.
        /// </summary>
        /// <example>
        /// <c>https://mycluster.westus2.kusto.windows.net</c>
        /// → <c>https://ingest-mycluster.westus2.kusto.windows.net</c>
        /// </example>
        private static string DeriveIngestUrlFromClusterUrl(string clusterUrl)
        {
            var uri  = new Uri(clusterUrl);
            var host = uri.Host;
            if (!host.StartsWith("ingest-", StringComparison.OrdinalIgnoreCase))
            {
                host = "ingest-" + host;
            }

            return $"{uri.Scheme}://{host}";
        }

        private static bool TryDecideAuthMethod(
            string ingestUri,
            Action<string> errorLogger,
            bool allowInteractiveAuth,
            string clientId,
            string tenantId,
            string serviceConnectionId,
            out UserRequestedAuthenticationMethod userRequestedAuthenticationMethod)
        {
            userRequestedAuthenticationMethod = default;
            
            // Workload identity federation
            // If a serviceConnectionId is specified, tenantId and clientId are required for workload identity federation
            if (!string.IsNullOrWhiteSpace(serviceConnectionId))
            {
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    errorLogger($"Tenant id is missing for workload identity federation in '{ingestUri}'. {s_identityFormat}");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(clientId))
                {
                    errorLogger($"Client id is missing for workload identity federation in '{ingestUri}'. {s_identityFormat}");
                    return false;
                }

                userRequestedAuthenticationMethod = UserRequestedAuthenticationMethod.WorkloadIdentityFederation;
                return true;
            }

            // Check for managed identity case
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                userRequestedAuthenticationMethod = UserRequestedAuthenticationMethod.ManagedIdentity;
                return true;
            }

            // Check for interactive auth case
            // In this case the tenantId and interactive auth are required
            if (!string.IsNullOrWhiteSpace(tenantId))
            { 
                if (!allowInteractiveAuth)
                {
                    errorLogger($"Interactive authentication was requested, but it is not enabled. If this is a developer build, consider passing /interactive+.");
                    return false;
                }

                userRequestedAuthenticationMethod = UserRequestedAuthenticationMethod.InteractiveAuthentication;
                return true;
            }

            // We exhausted all options and couldn't decide on an auth method
            errorLogger($"Could not determine authentication method for '{ingestUri}'. {s_identityFormat}");
            return false;
        }
    }
}