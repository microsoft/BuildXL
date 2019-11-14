// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        ///     Used to compute drop expiration date (<see cref="Microsoft.VisualStudio.Services.Drop.App.Core.IDropServiceClient.CreateAsync"/>).
        /// </summary>
        public TimeSpan Retention { get; }

        /// <summary>
        ///     Timeout for http requests (<see cref="Microsoft.VisualStudio.Services.Content.Common.ArtifactHttpClientFactory.ArtifactHttpClientFactory"/>).
        /// </summary>
        public TimeSpan HttpSendTimeout { get; }

        /// <summary>
        ///     Enable verbose logging.
        /// </summary>
        public bool Verbose { get; }

        /// <summary>
        ///     Enable drop telemetry.
        /// </summary>
        public bool EnableTelemetry { get; }

        /// <summary>
        ///     Enable chunk dedup.
        /// </summary>
        public bool EnableChunkDedup { get; }

        /// <summary>
        ///     Log directory.
        /// </summary>
        public string LogDir { get; }

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
        public static bool DefaultVerbose { get; } = false;

        /// <nodoc/>
        public static bool DefaultEnableTelemetry { get; } = false;

        /// <nodoc/>
        public static bool DefaultEnableChunkDedup { get; } = false;
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
            bool? verbose = null,
            bool? enableTelemetry = null,
            bool? enableChunkDedup = null,
            string logDir = null)
        {
            Name = dropName;
            Service = serviceEndpoint;
            MaxParallelUploads = maxParallelUploads ?? DefaultMaxParallelUploads;
            Retention = retention ?? DefaultRetention;
            HttpSendTimeout = httpSendTimeout ?? DefaultHttpSendTimeout;
            Verbose = verbose ?? DefaultVerbose;
            EnableTelemetry = enableTelemetry ?? DefaultEnableTelemetry;
            EnableChunkDedup = enableChunkDedup ?? DefaultEnableChunkDedup;
            LogDir = logDir;
        }
    }
}
