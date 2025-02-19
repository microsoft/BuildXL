// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace BuildToolsInstaller.Config
{
    /// <summary>
    /// On distributed builds, an 'Orchestrator' agent resolves the version to use,
    /// and a 'Worker' agent retrieves the resolved version from the build properties
    /// </summary>
    public enum DistributedRole
    {
        Orchestrator,
        Worker
    }

    /// <summary>
    /// Specific configuration for the BuildXL installer
    /// Consumed from a JSON file
    /// </summary>
    public class BuildXLNugetInstallerConfig
    {
        /// <summary>
        /// A specific version to install
        /// TODO [maly]: Deprecate this!! It's used by 1JS now so we need to migrate them first
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// If worker mode is true, the installer will poll the build properties to resolve
        /// the BuildXL version. If it's false, the installer will push the resolved version
        /// to the build properties
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DistributedRole? DistributedRole {  get; set; }

        /// <summary>
        /// Used as a prefix for all build property keys. Used to disambiguate jobs within the same ADO build 
        /// </summary>
        public string? InvocationKey { get; set; }

        /// <summary>
        /// Time to wait for the orchestrator information. Only applies in Worker distributed mode.
        /// </summary>
        public int? WorkerTimeoutMin {  get; set; }

        /// <summary>
        /// Specific arguments for the BuildXLNugetInstaller are given as a string in the main configuration
        /// Here, we interpret that string as serializing this object as a semi-colon separated list of key-value pairs
        /// </summary>
        internal static BuildXLNugetInstallerConfig? DeserializeFromString(string extraConfiguration, ILogger logger)
        {
            // The configuration should be serialized as a semicolon-separated list of key-value pairs
            // We compare against the property names in a case insensitive manner
            var properties = extraConfiguration.Split(';');
            var config = new BuildXLNugetInstallerConfig();
            foreach (var property in properties)
            {
                if (string.IsNullOrEmpty(property))
                {
                    continue;
                }

                // If it's not a valid key-value pair, ignore
                var keyValue = property.Split('=');
                if (keyValue.Length != 2)
                {
                    // Ignore
                    logger.Error(property + " is not a valid argument for BuildXLNugetInstallerConfig");
                    return null;
                }

                var key = keyValue[0].Trim().ToLower();
                var value = keyValue[1].Trim();
                switch (key)
                {
                    case "version":
                        config.Version = value;
                        break;
                    case "distributedrole":
                        if (!Enum.TryParse(value, true, out DistributedRole role))
                        {
                            logger.Error($"Unknown {nameof(DistributedRole)}: " + key + " deserializing BuildXLNugetInstallerConfig");
                            return null;
                        }
                        config.DistributedRole = role;
                        break;
                    case "invocationkey":
                        config.InvocationKey = value;
                        break;
                    case "workertimeoutmin":
                        if (!int.TryParse(value, out int timeout))
                        {
                            logger.Error($"Invalid value for {nameof(WorkerTimeoutMin)}: " + key + ".");
                            return null;
                        }
                        config.WorkerTimeoutMin = timeout;
                        break;
                    default:
                        // Ignore
                        logger.Error("Encountered unknown property " + key + " while deserializing BuildXLNugetInstallerConfig");
                        return null;
                }
            }

            return config;
        }
    }
}
