// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

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

        public static HostParameters FromEnvironment()
        {
            var result = new HostParameters()
            {
                ServiceDir = getValue("ServiceDir"),
                Environment = getValue("Environment"),
                Stamp = getValue("Stamp"),
                Ring = getValue("Ring"),
                Machine = getValue("Machine"),
                Region = getValue("Region"),
                MachineFunction = getValue("MachineFunction")
            };

            return result;

            string getValue(string name)
            {
                var value = System.Environment.GetEnvironmentVariable(HostPrefix + name);
                return !string.IsNullOrEmpty(value) ? value : "Default";
            }
        }

        public IDictionary<string, string> ToEnvironment()
        {
            var env = new Dictionary<string, string>();
            setValue("Environment", Environment);
            setValue("Stamp", Stamp);
            setValue("Ring", Ring);
            setValue("Machine", Machine);
            setValue("Region", Region);
            setValue("MachineFunction", MachineFunction);
            setValue("ServiceDir", ServiceDir);

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
    }

    public class DeploymentParameters : HostParameters
    {
        public Guid ContextId { get; set; } = Guid.NewGuid();
        public string AuthorizationSecretName { get; set; }
        public string AuthorizationSecret { get; set; }
        public bool GetContentInfoOnly { get; set; }
        public bool GetContentIdOnly { get; set; }
    }
}
