// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// A very simple telemetry provider for Kusto log uploads.
    /// </summary>
    /// <remarks>
    /// Only provides machine name, since it doesn't assume the build running on CloudBuild
    /// </remarks>
    public class BasicTelemetryFieldsProvider : ITelemetryFieldsProvider
    {
        private readonly string? _buildId;
        /// <nodoc/>
        public BasicTelemetryFieldsProvider(string? buildId = null)
        {
            _buildId = buildId;
        }

        /// <nodoc/>
        public string BuildId => _buildId ?? "Unknown";

        /// <nodoc/>
        public string ServiceName => "None";

        /// <nodoc/>
        public string APEnvironment => "None";

        /// <nodoc/>
        public string APCluster => "None";

        /// <nodoc/>
        public string APMachineFunction => "None";

        /// <nodoc/>
        public string MachineName => Environment.MachineName;

        /// <nodoc/>
        public string ServiceVersion => "None";

        /// <nodoc/>
        public string Stamp => "None";

        /// <nodoc/>
        public string Ring => "None";

        /// <nodoc/>
        public string ConfigurationId => "None";
    }
}
