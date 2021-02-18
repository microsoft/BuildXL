// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.Monitor.App
{
    /// <nodoc />
    public enum MonitorEnvironment
    {
        CloudBuildContinuousIntegration,
        CloudBuildTest,
        CloudBuildProduction,
    }

    internal static class EnvExtensions
    {
        public static string Abbreviation(this MonitorEnvironment environment) => environment switch
        {
            MonitorEnvironment.CloudBuildContinuousIntegration => "ci",
            MonitorEnvironment.CloudBuildProduction => "prod",
            MonitorEnvironment.CloudBuildTest => "test",
            _ => throw new NotImplementedException($"Environment `{environment}` does not have an abbreviation"),
        };
    }
}
