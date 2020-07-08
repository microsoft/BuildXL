// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.Monitor.App
{
    public struct StampId : IEquatable<StampId>
    {
        public CloudBuildEnvironment Environment { get; }

        public string Name { get; }

        public StampId(CloudBuildEnvironment environment, string name)
        {
            Environment = environment;
            Name = name;
        }

        public string PrimaryRedisName
        {
            get
            {
                var stamp = AzureCompatibleStampName;
                if (Environment == CloudBuildEnvironment.ContinuousIntegration)
                {
                    stamp = "mwci";
                }

                return $"cbcache-{Environment.Abbreviation()}-redis-{stamp}";
            }
        }

        public string SecondaryRedisName
        {
            get
            {
                var stamp = AzureCompatibleStampName;
                if (Environment == CloudBuildEnvironment.ContinuousIntegration)
                {
                    stamp = "mwci";
                }

                return $"cbcache-{Environment.Abbreviation()}-redis-secondary-{stamp}";
            }
        }

        public string Datacenter => Name.Split(new string[] { "_" }, StringSplitOptions.None)[0];

        public string AzureCompatibleStampName => Name.Replace("_", "").ToLowerInvariant();

        public override string ToString()
        {
            return $"{Environment}/{Name}";
        }

        public override bool Equals(object? obj)
        {
            return obj is StampId item && Equals(item);
        }

        public bool Equals(StampId stampId)
        {
            return Environment == stampId.Environment && Name.Equals(stampId.Name, StringComparison.InvariantCultureIgnoreCase);
        }

        public override int GetHashCode() => (Environment, Name).GetHashCode();
    }
}
