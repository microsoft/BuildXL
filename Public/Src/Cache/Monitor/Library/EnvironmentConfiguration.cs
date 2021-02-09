// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Monitor.App
{
    public class EnvironmentConfiguration
    {
        public string AzureTenantId { get; internal set; } = string.Empty;

        public string AzureAppId { get; internal set; } = string.Empty;

        public string AzureAppKey { get; set; } = string.Empty;

        public string AzureSubscriptionName { get; internal set; } = string.Empty;

        public string AzureSubscriptionId { get; internal set; } = string.Empty;

        public string KustoClusterUrl { get; internal set; } = string.Empty;

        public string KustoDatabaseName { get; internal set; } = string.Empty;
    }
}
