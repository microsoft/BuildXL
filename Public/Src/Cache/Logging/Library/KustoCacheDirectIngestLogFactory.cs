// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Utilities.Core.Tracing;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// Creates a <see cref="KustoDirectIngestLog"/> for BuildXL cache logs, with the appropriate table name, mapping name, table schema, and mapping JSON."/>
    /// </summary>
    public sealed class KustoCacheDirectIngestLogFactory
    {
        private const string CacheTableName = "BuildXLCacheLogs";
        private const string CacheMappingName = "BuildXLCacheIngestion";

        // Cache table: CSV format, 25 columns matching the NLog layout
        private const string CacheTableSchema =
            "(PreciseTimeStamp:datetime, LocalPreciseTimeStamp:datetime, CorrelationId:string, " +
            "Role:string, Component:string, Operation:string, Arguments:string, Duration:timespan, " +
            "Result:string, BuildId:string, Message:string, Exception:string, ProcessId:long, " +
            "ThreadId:long, Machine:string, Stamp:string, Ring:string, ConfigurationId:string, " +
            "Service:string, ServiceVersion:string, CacheVersion:string, ProcessStartTimeUtc:datetime, " +
            "LogLevel:int, MachineFunction:string, Environment:string)";

        private const string CacheMappingJson =
            "[" +
            "{\"column\":\"PreciseTimeStamp\",\"Properties\":{\"Ordinal\":\"0\"}}" +
            ",{\"column\":\"LocalPreciseTimeStamp\",\"Properties\":{\"Ordinal\":\"1\"}}" +
            ",{\"column\":\"CorrelationId\",\"Properties\":{\"Ordinal\":\"2\"}}" +
            ",{\"column\":\"Role\",\"Properties\":{\"Ordinal\":\"3\"}}" +
            ",{\"column\":\"Component\",\"Properties\":{\"Ordinal\":\"4\"}}" +
            ",{\"column\":\"Operation\",\"Properties\":{\"Ordinal\":\"5\"}}" +
            ",{\"column\":\"Arguments\",\"Properties\":{\"Ordinal\":\"6\"}}" +
            ",{\"column\":\"Duration\",\"Properties\":{\"Ordinal\":\"7\"}}" +
            ",{\"column\":\"Result\",\"Properties\":{\"Ordinal\":\"8\"}}" +
            ",{\"column\":\"BuildId\",\"Properties\":{\"Ordinal\":\"9\"}}" +
            ",{\"column\":\"Message\",\"Properties\":{\"Ordinal\":\"10\"}}" +
            ",{\"column\":\"Exception\",\"Properties\":{\"Ordinal\":\"11\"}}" +
            ",{\"column\":\"ProcessId\",\"Properties\":{\"Ordinal\":\"12\"}}" +
            ",{\"column\":\"ThreadId\",\"Properties\":{\"Ordinal\":\"13\"}}" +
            ",{\"column\":\"Machine\",\"Properties\":{\"Ordinal\":\"14\"}}" +
            ",{\"column\":\"Stamp\",\"Properties\":{\"Ordinal\":\"15\"}}" +
            ",{\"column\":\"Ring\",\"Properties\":{\"Ordinal\":\"16\"}}" +
            ",{\"column\":\"ConfigurationId\",\"Properties\":{\"Ordinal\":\"17\"}}" +
            ",{\"column\":\"Service\",\"Properties\":{\"Ordinal\":\"18\"}}" +
            ",{\"column\":\"ServiceVersion\",\"Properties\":{\"Ordinal\":\"19\"}}" +
            ",{\"column\":\"CacheVersion\",\"Properties\":{\"Ordinal\":\"20\"}}" +
            ",{\"column\":\"ProcessStartTimeUtc\",\"Properties\":{\"Ordinal\":\"21\"}}" +
            ",{\"column\":\"LogLevel\",\"Properties\":{\"Ordinal\":\"22\"}}" +
            ",{\"column\":\"MachineFunction\",\"Properties\":{\"Ordinal\":\"23\"}}" +
            ",{\"column\":\"Environment\",\"Properties\":{\"Ordinal\":\"24\"}}" +
            "]";

        /// <see cref="KustoDirectIngestLog.TryCreate"/>
        public static KustoDirectIngestLog? TryCreateForCache(
            Guid sessionId,
            string ingestUri,
            bool allowInteractiveAuth,
            Action<string> errorLogger,
            Action<string> debugLogger,
            IConsole console,
            CancellationToken cancellationToken)
        {
            return KustoDirectIngestLog.TryCreate(
                sessionId,
                ingestUri,
                allowInteractiveAuth,
                errorLogger,
                debugLogger,
                console,
                CacheTableName,
                CacheMappingName,
                CacheTableSchema,
                CacheMappingJson,
                Kusto.Data.Common.DataSourceFormat.csv,
                cancellationToken);
        }
    }
}
