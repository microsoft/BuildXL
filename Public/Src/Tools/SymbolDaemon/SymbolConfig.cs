// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

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
        /// Timeout for http requests (<see cref="Microsoft.VisualStudio.Services.Content.Common.ArtifactHttpClientFactory.ArtifactHttpClientFactory"/>).
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

        /// <nodoc/>
        public static TimeSpan DefaultRetention { get; } = TimeSpan.FromDays(10);

        /// <nodoc/>
        public static TimeSpan DefaultHttpSendTimeout { get; } = TimeSpan.FromMinutes(10);

        /// <nodoc/>
        public static bool DefaultVerbose { get; } = false;

        /// <nodoc/>
        public static bool DefaultEnableTelemetry { get; } = false;

        /// <nodoc />
        public SymbolConfig(
            string requestName,
            Uri serviceEndpoint,
            TimeSpan? retention = null,
            TimeSpan? httpSendTimeout = null,
            bool? verbose = null,
            bool? enableTelemetry = null,
            string logDir = null)
        {
            Name = requestName;
            Service = serviceEndpoint;
            Retention = retention ?? DefaultRetention;
            HttpSendTimeout = httpSendTimeout ?? DefaultHttpSendTimeout;
            Verbose = verbose ?? DefaultVerbose;
            EnableTelemetry = enableTelemetry ?? DefaultEnableTelemetry;
            LogDir = logDir;
        }
    }
}