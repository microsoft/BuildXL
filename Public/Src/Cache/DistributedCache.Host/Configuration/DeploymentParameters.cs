// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

#nullable disable

namespace BuildXL.Cache.Host.Configuration
{
    public class HostParameters
    {
        private const string HostPrefix = "BuildXL.Cache.Host.";

        public string ServiceDir { get; set; }
        public string Environment { get; set; }
        public string Stamp { get; set; }
        public string Ring { get; set; }
        public string Machine { get; set; } = System.Environment.MachineName;
        public string Region { get; set; }
        public string MachineFunction { get; set; }
        public string ServiceVersion { get; set; }
        public string ConfigurationId { get; set; }
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;

        public Dictionary<string, string> Properties { get; set; } = new();
        public Dictionary<string, string[]> Flags { get; set; } = new();


        /// <summary>
        /// Creates and instance of <see cref="HostParameters"/> from the environment variables (unless <paramref name="testEnvironment"/> is not null, then this dictionary is used as a storage of environment variables).
        /// </summary>
        public static HostParameters FromEnvironment(IDictionary<string, string> testEnvironment = null)
        {
            var result = new HostParameters()
            {
                ServiceDir = getValueOrDefault(nameof(ServiceDir)),
                Environment = getValueOrDefault(nameof(Environment)),
                Stamp = getValueOrDefault(nameof(Stamp)),
                Ring = getValueOrDefault(nameof(Ring)),
                Machine = getValueOrDefault(nameof(Machine)),
                Region = getValueOrDefault(nameof(Region)),
                MachineFunction = getValueOrDefault(nameof(MachineFunction)),
                ServiceVersion = getValueOrDefault(nameof(ServiceVersion)),
                // Not using the default value for ConfigurationId.
                ConfigurationId = getValue(nameof(ConfigurationId)),
            };

            return result;

            string getValueOrDefault(string name)
            {
                string value = getValue(name);
                
                return !string.IsNullOrEmpty(value) ? value : "Default";
            }

            string getValue(string name)
            {
                string value;
                if (testEnvironment is not null)
                {
                    testEnvironment.TryGetValue(HostPrefix + name, out value);
                }
                else
                {
                    value = System.Environment.GetEnvironmentVariable(HostPrefix + name);
                }

                return string.IsNullOrEmpty(value) ? null : value;
            }
        }

        /// <summary>
        /// Stores the host parameter as environment variables.
        /// </summary>
        /// <remarks>
        /// If <paramref name="saveConfigurationId"/> is true, then <see cref="ConfigurationId"/> is stored as well.
        /// The separate is required because Launcher should not propagate the ConfigurationId, but the OutOfProc CaSaaS should.
        /// </remarks>
        public IDictionary<string, string> ToEnvironment(bool saveConfigurationId)
        {
            var env = new Dictionary<string, string>();
            if (saveConfigurationId)
            {
                setValue(nameof(ConfigurationId), ConfigurationId);
            }

            setValue(nameof(ServiceVersion), ServiceVersion);
            setValue(nameof(MachineFunction), MachineFunction);
            setValue(nameof(Environment), Environment);
            setValue(nameof(Stamp), Stamp);
            setValue(nameof(Ring), Ring);
            setValue(nameof(Machine), Machine);
            setValue(nameof(Region), Region);
            setValue(nameof(MachineFunction), MachineFunction);
            setValue(nameof(ServiceDir), ServiceDir);

            void setValue(string name, string value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    env[HostPrefix + name] = value;
                }
            }

            return env;
        }

        public override string ToString()
        {
            return $"Machine={Machine} Stamp={Stamp}";
        }

        public void ApplyFromTelemetryProviderIfNeeded(ITelemetryFieldsProvider telemetryProvider)
        {
            if (telemetryProvider is null)
            {
                return;
            }

            Ring ??= telemetryProvider.Ring;
            Stamp ??= telemetryProvider.Stamp;
            Machine ??= telemetryProvider.MachineName;
            MachineFunction ??= telemetryProvider.APMachineFunction;
            Environment ??= telemetryProvider.APEnvironment;
            ServiceVersion ??= telemetryProvider.ServiceVersion;
            ConfigurationId ??= telemetryProvider.ConfigurationId;
        }

        public static HostParameters FromTelemetryProvider(ITelemetryFieldsProvider telemetryProvider)
        {
            Contract.Requires(telemetryProvider is not null);

            var result = new HostParameters();
            result.ApplyFromTelemetryProviderIfNeeded(telemetryProvider);

            return result;
        }
    }

    public class DeploymentParameters : HostParameters
    {
        public string ContextId { get; set; } = Guid.NewGuid().ToString();
        public string AuthorizationSecretName { get; set; }
        public string AuthorizationSecret { get; set; }
        public bool GetContentInfoOnly { get; set; }

        /// <summary>
        /// Indicates whether deployment client should bypass up to date check
        /// </summary>
        public bool ForceUpdate;
    }
}
