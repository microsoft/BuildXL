// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Utilities.Core.Tracing;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL
{
    /// <summary>
    /// Creates a <see cref="KustoDirectIngestLog"/> for BuildXL engine logs, with the appropriate table name, mapping name, table schema, and mapping JSON."/>
    /// </summary>
    public sealed class KustoEngineDirectIngestLogFactory
    {
        private const string EngineTableName = "BuildXLLogs";
        private const string EngineMappingName = "BuildXLIngestion";

        /// <summary>
        /// Engine table: PSV format, 9 columns.
        /// </summary>
        private const string EngineTableSchema =
            "(Timestamp:datetime, Level:int, SessionId:guid, ActivityId:guid, " +
            "RelatedActivityId:guid, EventNumber:int, Machine:string, IsWorker:bool, Message:string)";

        private const string EngineMappingJson =
            "[" +
            "{\"Column\":\"Timestamp\",\"Properties\":{\"Ordinal\":\"0\"}}" +
            ",{\"Column\":\"Level\",\"Properties\":{\"Ordinal\":\"1\"}}" +
            ",{\"Column\":\"SessionId\",\"Properties\":{\"Ordinal\":\"2\"}}" +
            ",{\"Column\":\"ActivityId\",\"Properties\":{\"Ordinal\":\"3\"}}" +
            ",{\"Column\":\"RelatedActivityId\",\"Properties\":{\"Ordinal\":\"4\"}}" +
            ",{\"Column\":\"EventNumber\",\"Properties\":{\"Ordinal\":\"5\"}}" +
            ",{\"Column\":\"Machine\",\"Properties\":{\"Ordinal\":\"6\"}}" +
            ",{\"Column\":\"IsWorker\",\"Properties\":{\"Ordinal\":\"7\"}}" +
            ",{\"Column\":\"Message\",\"Properties\":{\"Ordinal\":\"8\"}}" +
            "]";

        /// <see cref="KustoDirectIngestLog.TryCreate"/>
        public static KustoDirectIngestLog? TryCreateForEngine(
            Guid relatedActivityId,
            string ingestUri,
            bool allowInteractiveAuth,
            Action<string> errorLogger,
            Action<string> debugLogger,
            IConsole console,
            CancellationToken cancellationToken)
        {
            return KustoDirectIngestLog.TryCreate(
                relatedActivityId,
                ingestUri,
                allowInteractiveAuth,
                errorLogger,
                debugLogger,
                console,
                EngineTableName,
                EngineMappingName,
                EngineTableSchema,
                EngineMappingJson,
                Kusto.Data.Common.DataSourceFormat.psv,
                cancellationToken);
        }
    }
}
