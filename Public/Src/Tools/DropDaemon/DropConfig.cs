// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Tool.DropDaemon
{
    /// <summary>
    ///     Drop configuration.
    /// </summary>
    public sealed class DropConfig
    {
        #region ConfigOptions

        /// <summary>
        ///     Drop name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Drop service to connect to.
        /// </summary>
        public Uri Service { get; }

        /// <summary>
        ///     Size of batches in which to send 'associate' requests to drop service endpoint.
        /// </summary>
        public int BatchSize = DefaultBatchSizeForAssociate;

        /// <summary>
        ///     Maximum number of uploads to issue to drop service endpoint in parallel.
        /// </summary>
        public int MaxParallelUploads { get; }

        /// <summary>
        ///     Maximum time in milliseconds to wait before triggering a batch 'associate' request.
        /// </summary>
        public TimeSpan NagleTime = DefaultNagleTimeForAssociate;

        /// <summary>
        ///     Used to compute drop expiration date (<see cref="Microsoft.VisualStudio.Services.Drop.App.Core.IDropServiceClient.CreateAsync(string, bool, DateTime?, bool, System.Threading.CancellationToken)"/>).
        /// </summary>
        public TimeSpan Retention { get; }

        /// <summary>
        ///     Timeout for http requests (<see cref="Microsoft.VisualStudio.Services.Content.Common.ArtifactHttpClientFactory.ArtifactHttpClientFactory(Microsoft.VisualStudio.Services.Common.VssCredentials, TimeSpan?, Microsoft.VisualStudio.Services.Content.Common.Tracing.IAppTraceSource, System.Threading.CancellationToken)"/>).
        /// </summary>
        public TimeSpan HttpSendTimeout { get; }

        /// <summary>
        ///     Enable drop telemetry.
        /// </summary>
        public bool EnableTelemetry { get; }

        /// <summary>
        ///     Enable chunk dedup.
        /// </summary>
        public bool EnableChunkDedup { get; }

        /// <summary>
        ///     File name for artifact-side logs
        /// </summary>
        public string ArtifactLogName { get; }

        /// <summary>
        ///     Optional domain id. Null represents a default value.
        /// </summary>
        public byte? DomainId { get; }

        /// <summary>
        ///     Build Manifest generation flag.
        /// </summary>
        public bool GenerateBuildManifest { get; }

        /// <summary>
        ///     Build Manifest Signing flag.
        /// </summary>
        public bool SignBuildManifest { get; }

        /// <summary>
        ///     Optional custom SBOM Package Name.
        /// </summary>
        public string SbomPackageName { get; }
        
        /// <summary>
        ///     Optional custom SBOM Package version.
        /// </summary>
        public string SbomPackageVersion { get; }

        /// <summary>
        ///     Whether to report telemetry for individual drops.
        /// </summary>
        public bool ReportTelemetry { get; }
        #endregion

        #region Defaults

        /// <nodoc/>
        public static Uri DefaultServiceEndpoint { get; } = new Uri("https://artifactsu0.artifacts.visualstudio.com/DefaultCollection");

        /// <nodoc/>
        public const int DefaultBatchSizeForAssociate = 300;

        /// <nodoc/>
        public static int DefaultMaxParallelUploads { get; } = Environment.ProcessorCount;

        /// <nodoc/>
        public static readonly TimeSpan DefaultNagleTimeForAssociate = TimeSpan.FromMilliseconds(300);

        /// <nodoc/>
        public static TimeSpan DefaultRetention { get; } = TimeSpan.FromDays(10);

        /// <nodoc/>
        public static TimeSpan DefaultHttpSendTimeout { get; } = TimeSpan.FromMinutes(10);

        /// <nodoc/>
        public static bool DefaultEnableTelemetry { get; } = false;

        /// <nodoc/>
        public static bool DefaultEnableChunkDedup { get; } = false;

        /// <nodoc/>
        public static bool DefaultGenerateBuildManifest { get; } = true;

        /// <nodoc/>
        public static bool DefaultSignBuildManifest { get; } = true;
        #endregion

        // ==================================================================================================
        // Constructor
        // ==================================================================================================

        /// <nodoc/>
        public DropConfig(
            string dropName,
            Uri serviceEndpoint,
            int? maxParallelUploads = null,
            TimeSpan? retention = null,
            TimeSpan? httpSendTimeout = null,
            bool? enableTelemetry = null,
            bool? enableChunkDedup = null,
            string artifactLogName = null,
            int? batchSize = null,
            byte? dropDomainId = null,
            bool? generateBuildManifest = null,
            bool? signBuildManifest = null,
            string sbomPackageName = null,
            string sbomPackageVersion = null,
            bool? reportTelemetry = null)
        {
            Name = dropName;
            Service = serviceEndpoint;
            MaxParallelUploads = maxParallelUploads ?? DefaultMaxParallelUploads;
            Retention = retention ?? DefaultRetention;
            HttpSendTimeout = httpSendTimeout ?? DefaultHttpSendTimeout;
            EnableTelemetry = enableTelemetry ?? DefaultEnableTelemetry;
            EnableChunkDedup = enableChunkDedup ?? DefaultEnableChunkDedup;
            ArtifactLogName = artifactLogName;
            BatchSize = batchSize ?? DefaultBatchSizeForAssociate;
            DomainId = dropDomainId;
            GenerateBuildManifest = generateBuildManifest ?? DefaultGenerateBuildManifest;
            SignBuildManifest = signBuildManifest ?? DefaultSignBuildManifest;
            SbomPackageName = sbomPackageName;
            SbomPackageVersion = sbomPackageVersion;
            ReportTelemetry = reportTelemetry ?? false;
        }
    }
}
