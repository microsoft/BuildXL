// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL
{
    /// <summary>
    /// A very simple telemetry provider for kusto log uploads. <see cref="BlobWriterEventListener"/>
    /// </summary>
    /// <remarks>
    /// Only provides machine name, since it doesn't assume the build running on CloudBuild
    /// </remarks>
    internal class BasicTelemetryFieldsProvider : ITelemetryFieldsProvider
    {
        public BasicTelemetryFieldsProvider() { }

        public string BuildId => "Unknown";

        public string ServiceName => "None";

        public string APEnvironment => "None";

        public string APCluster => "None";

        public string APMachineFunction => "None";

        public string MachineName => Environment.MachineName;

        public string ServiceVersion => "None";

        public string Stamp => "None";

        public string Ring => "None";

        public string ConfigurationId => "None";
    }
}
