// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Host.Configuration;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Telemetry fields provider based on <see cref="HostParameters"/>
    /// </summary>
    public class HostTelemetryFieldsProvider : ITelemetryFieldsProvider
    {
        private readonly HostParameters _hostParmeters;

        public string BuildId => "Unknown";

        public string ServiceName { get; set; } = "CacheService";

        public string APEnvironment => "Unknown";

        public string APCluster => "None";

        public string APMachineFunction => _hostParmeters.MachineFunction;

        public string MachineName => Environment.MachineName;

        public string ServiceVersion => "None";

        public string Stamp => _hostParmeters.Stamp;

        public string Ring => _hostParmeters.Ring;

        public string ConfigurationId { get; set; } = "None";

        public HostTelemetryFieldsProvider(HostParameters hostParameters)
        {
            _hostParmeters = hostParameters;
        }
    }
}
