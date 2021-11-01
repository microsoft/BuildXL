// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Interfaces.Logging
{
    /// <summary>
    /// Provides CASaaS with basic information about the environment it runs in. This is used to obtain logging
    /// information.
    /// </summary>
    /// <remarks>
    /// See CloudCacheLogEvent.kql to understand where these fields go in actual telemetry.
    /// </remarks>
    public interface ITelemetryFieldsProvider
    {
        /// <summary>
        /// This property is deprecated and should not be used.
        /// Instead <see cref="GlobalInfoStorage.GetGlobalInfo(GlobalInfoKey)"/>.
        /// </summary>
        string BuildId { get; }

        /// <nodoc />
        string APEnvironment { get; }

        /// <nodoc />
        string APCluster { get; }

        /// <nodoc />
        string APMachineFunction { get; }

        /// <nodoc />
        string MachineName { get; }

        /// <nodoc />
        string ServiceName { get; }

        /// <nodoc />
        string ServiceVersion { get; }

        /// <nodoc />
        string Ring { get; }

        /// <nodoc />
        string Stamp { get; }

        /// <nodoc />
        string ConfigurationId { get; }
    }
}
