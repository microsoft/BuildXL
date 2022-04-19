// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.Host.Configuration;
using Xunit;

namespace BuildXL.Cache.Host.Test
{
    public class DeploymentParametersTests
    {
        [Fact]
        public void TestRoundTrip()
        {
            var h1 = new HostParameters()
                     {
                         ConfigurationId = "1",
                         Environment = "2",
                         Machine = "3",
                         MachineFunction = "4",
                         Region = "5",
                         Ring = "6",
                         ServiceDir = "7",
                         ServiceVersion = "8",
                         Stamp = "9"
                     };

            var environment = h1.ToEnvironment(saveConfigurationId: true);
            var h2 = HostParameters.FromEnvironment(environment);

            Assert.Equal(h1, h2, HostParametersComparer.Instance);

            environment = h1.ToEnvironment(saveConfigurationId: false);
            var h3 = HostParameters.FromEnvironment(environment);
            Assert.Null(h3.ConfigurationId);

            // h3 does not have correct 'ConfigurationId'.
            Assert.NotEqual(h1, h3, HostParametersComparer.Instance);

            h3.ConfigurationId = h1.ConfigurationId;
            // And now they should be equal
            Assert.Equal(h1, h3, HostParametersComparer.Instance);
        }

        private class HostParametersComparer : IEqualityComparer<HostParameters>
        {
            public static HostParametersComparer Instance { get; } = new HostParametersComparer();

            public bool Equals(HostParameters x, HostParameters y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null)
                {
                    return false;
                }

                if (y is null)
                {
                    return false;
                }

                if (x.GetType() != y.GetType())
                {
                    return false;
                }

                return x.ServiceDir == y.ServiceDir && x.Environment == y.Environment && x.Stamp == y.Stamp && x.Ring == y.Ring &&
                       x.Machine == y.Machine && x.Region == y.Region && x.MachineFunction == y.MachineFunction &&
                       x.ServiceVersion == y.ServiceVersion && x.ConfigurationId == y.ConfigurationId;
            }

            public int GetHashCode(HostParameters obj)
            {
                unchecked
                {
                    var hashCode = (obj.ServiceDir != null ? obj.ServiceDir.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (obj.Environment != null ? obj.Environment.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (obj.Stamp != null ? obj.Stamp.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (obj.Ring != null ? obj.Ring.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (obj.Machine != null ? obj.Machine.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (obj.Region != null ? obj.Region.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (obj.MachineFunction != null ? obj.MachineFunction.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (obj.ServiceVersion != null ? obj.ServiceVersion.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (obj.ConfigurationId != null ? obj.ConfigurationId.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (obj.Properties != null ? obj.Properties.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (obj.Flags != null ? obj.Flags.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }

    }
}
