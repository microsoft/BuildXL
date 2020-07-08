// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.Monitor.App
{
    /// <nodoc />
    public enum CloudBuildEnvironment
    {
        ContinuousIntegration,
        Test,
        Production,
    }

    internal static class EnvExtensions
    {
        public static string Abbreviation(this CloudBuildEnvironment environment) => environment switch
        {
            CloudBuildEnvironment.ContinuousIntegration => "ci",
            CloudBuildEnvironment.Production => "prod",
            CloudBuildEnvironment.Test => "test",
            _ => throw new NotImplementedException($"Environment `{environment}` does not have an abbreviation"),
        };
    }
}
