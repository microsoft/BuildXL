// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Monitor.App
{
    public class AzureActiveDirectoryCredentials
    {
        public string TenantId { get; internal set; } = string.Empty;

        public string AppId { get; internal set; } = string.Empty;

        public string AppKey { get; set; } = string.Empty;
    }

    public class AzureCredentials
    {
        public AzureActiveDirectoryCredentials Credentials { get; internal set; } = new AzureActiveDirectoryCredentials();

        public string SubscriptionId { get; internal set; } = string.Empty;
    }

    public class KustoCredentials
    {
        public AzureActiveDirectoryCredentials Credentials { get; internal set; } = new AzureActiveDirectoryCredentials();

        public string ClusterUrl { get; internal set; } = string.Empty;
    }

    public class EnvironmentConfiguration
    {
        public AzureCredentials AzureCredentials { get; internal set; } = new AzureCredentials();

        public KustoCredentials KustoCredentials { get; internal set; } = new KustoCredentials();

        public string KustoDatabaseName { get; internal set; } = string.Empty;
    }
}
