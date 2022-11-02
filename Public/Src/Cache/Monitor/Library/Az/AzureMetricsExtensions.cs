// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.Monitor.Library.Az
{
    internal class MetricName
    {
        public string Name { get; }

        public Dictionary<string, string>? Group { get; }

        public MetricName(string name, Dictionary<string, string>? group = null)
        {
            Contract.RequiresNotNullOrEmpty(name);
            Name = name;
            Group = group;
        }

        public static implicit operator string(MetricName metric) => metric.ToString();

        public override string ToString()
        {
            if (Group != null && Group.Count > 0)
            {
                var groupAsString = string.Join(
                    ", ",
                    Group.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                return $"{Name}({groupAsString})";
            }

            return $"{Name}";
        }
    }
}
