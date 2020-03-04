#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Logging
{
    /// <summary>
    ///     Provides CASaaS with basic information about the environment it runs in. This is used to obtain logging
    ///     information.
    /// </summary>
    /// <remarks>
    ///     See CloudCacheLogEvent.kql to understand where these fields go in actual telemetry.
    /// </remarks>
    public interface ITelemetryFieldsProvider
    {
        /// <nodoc />
        public string BuildId { get; }

        /// <nodoc />
        public string APEnvironment { get; }

        /// <nodoc />
        public string APCluster { get; }

        /// <nodoc />
        public string APMachineFunction { get; }

        /// <nodoc />
        public string MachineName { get; }

        /// <nodoc />
        public string ServiceName { get; }

        /// <nodoc />
        public string ServiceVersion { get; }

        /// <nodoc />
        public string Ring { get; }

        /// <nodoc />
        public string Stamp { get; }

        /// <nodoc />
        public string ConfigurationId { get; }
    }
}
