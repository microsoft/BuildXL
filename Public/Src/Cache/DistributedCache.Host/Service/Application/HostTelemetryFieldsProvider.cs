// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Telemetry fields provider based on <see cref="HostParameters"/>
    /// </summary>
    public class HostTelemetryFieldsProvider : ITelemetryFieldsProvider
    {
        private readonly HostParameters _hostParameters;

        public string BuildId { get; set; }

        public string ServiceName { get; set; } = "CacheService";

        public string APEnvironment => _hostParameters.Environment;

        public string APCluster { get; set; } = "None";

        public string APMachineFunction => _hostParameters.MachineFunction;

        public string MachineName => Environment.MachineName;

        public string ServiceVersion => _hostParameters.ServiceVersion;

        public string Stamp => _hostParameters.Stamp;

        public string Ring => _hostParameters.Ring;

        public string ConfigurationId => _hostParameters.ConfigurationId;

        public HostTelemetryFieldsProvider(HostParameters hostParameters) => _hostParameters = hostParameters;
    }
}
