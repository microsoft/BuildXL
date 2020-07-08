// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Monitor.App
{
    public class EnvironmentConfiguration
    {
        public string KustoDatabaseName { get; internal set; } = string.Empty;

        public string AzureSubscriptionName { get; internal set; } = string.Empty;

        public string AzureSubscriptionId { get; internal set; } = string.Empty;

        public string AzureKeyVaultSubscriptionId { get; internal set; } = string.Empty;
    }
}
