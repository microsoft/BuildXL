// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.Services.Symbol.WebApi;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// Symbol publishing request config.
    /// </summary>
    public sealed class SymbolConfig
    {
        /// <summary>
        /// Request name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Symbol service endpoint
        /// </summary>
        public Uri Service { get; }

        /// <summary>
        /// Retention duration (<see cref="Microsoft.VisualStudio.Services.Symbol.App.Core.ISymbolServiceClient.FinalizeRequestAsync"/>)
        /// </summary>
        public TimeSpan Retention { get; }

        /// <summary>
        /// Timeout for http requests (<see cref="Microsoft.VisualStudio.Services.Content.Common.ArtifactHttpClientFactory.ArtifactHttpClientFactory(Microsoft.VisualStudio.Services.Common.VssCredentials, TimeSpan?, Microsoft.VisualStudio.Services.Content.Common.Tracing.IAppTraceSource, System.Threading.CancellationToken)"/>).
        /// </summary>
        public TimeSpan HttpSendTimeout { get; }

        /// <summary>
        /// Enable symbol telemetry
        /// </summary>
        public bool EnableTelemetry { get; }

        /// <summary>
        /// Log directory
        /// </summary>
        public string LogDir { get; }

        /// <summary>
        /// Enable verbose logging
        /// </summary>
        public bool Verbose { get; }

        /// <summary>
        /// Optional domain id. Null represents a default value.
        /// </summary>
        public byte? DomainId { get; }

        /// <summary>
        /// Size of batches in which to send 'associate' requests to the service endpoint.
        /// </summary>
        public int BatchSize { get; }

        /// <summary>
        /// Maximum number of uploads to issue to the service endpoint in parallel.
        /// </summary>
        public int MaxParallelUploads { get; }

        /// <summary>
        /// Maximum time to wait before triggering a current batch (i.e., processing a batch even if it's not completely full).
        /// </summary>
        public TimeSpan NagleTime { get; }

        /// <summary>
        /// Enable chunk dedup.
        /// </summary>
        /// <remarks>
        /// ChunkDedup is currently not supported, but the API already requires it.
        /// </remarks>
        public bool EnableChunkDedup => false;

        /// <summary>
        /// Whether to report collected telemetry.
        /// </summary>
        public bool ReportTelemetry { get; }

        /// <summary>
        /// The expected behavior when a debug entry to add already exists.
        /// </summary>
        public DebugEntryCreateBehavior DebugEntryCreateBehavior { get; }

        /// <nodoc/>
        public static TimeSpan DefaultRetention { get; } = TimeSpan.FromDays(10);

        /// <nodoc/>
        public static TimeSpan DefaultHttpSendTimeout { get; } = TimeSpan.FromMinutes(10);

        /// <nodoc/>
        public static bool DefaultVerbose { get; } = false;

        /// <nodoc/>
        public static bool DefaultEnableTelemetry { get; } = false;

        /// <nodoc/>
        public const int DefaultBatchSize = 100;

        /// <nodoc/>
        public static int DefaultMaxParallelUploads { get; } = Environment.ProcessorCount;

        /// <nodoc/>
        public static readonly TimeSpan DefaultNagleTime = TimeSpan.FromMilliseconds(300);

        /// <nodoc />
        public SymbolConfig(
            string requestName,
            Uri serviceEndpoint,
            string debugEntryCreateBehaviorStr = null,
            TimeSpan? retention = null,
            TimeSpan? httpSendTimeout = null,
            bool? verbose = null,
            bool? enableTelemetry = null,
            string logDir = null,
            byte? domainId = null,
            int? batchSize = null,
            int? maxParallelUploads = null,
            int? nagleTimeMs = null,
            bool? reportTelemetry = null)
        {
            Name = requestName;
            Service = serviceEndpoint;
            Retention = retention ?? DefaultRetention;
            HttpSendTimeout = httpSendTimeout ?? DefaultHttpSendTimeout;
            Verbose = verbose ?? DefaultVerbose;
            EnableTelemetry = enableTelemetry ?? DefaultEnableTelemetry;
            LogDir = logDir;
            DomainId = domainId;
            BatchSize = batchSize ?? DefaultBatchSize;
            NagleTime = nagleTimeMs.HasValue ? TimeSpan.FromMilliseconds(nagleTimeMs.Value) : DefaultNagleTime;
            MaxParallelUploads = maxParallelUploads ?? DefaultMaxParallelUploads;
            ReportTelemetry = reportTelemetry ?? false;

            if (debugEntryCreateBehaviorStr == null)
            {
                DebugEntryCreateBehavior = DebugEntryCreateBehavior.ThrowIfExists;
            }
            else if (Enum.TryParse(debugEntryCreateBehaviorStr, out DebugEntryCreateBehavior value))
            {
                DebugEntryCreateBehavior = value;
            }
            else
            {
                DebugEntryCreateBehavior = DebugEntryCreateBehavior.ThrowIfExists;
            }
        }
    }
}