// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Cache.Monitor.App
{
    internal static class Constants
    {
        public static string ServiceName { get; } = "ContentAddressableStoreService";

        public static string MasterServiceName { get; } = "ContentAddressableStoreMasterService";

        public static TimeSpan KustoIngestionDelay { get; } = TimeSpan.FromMinutes(20);

        public static string OldTableName { get; } = "CloudBuildLogEvent";

        public static string NewTableName { get; } = "CloudCacheLogEvent";

        public static string DefaultKustoClusterUrl { get; } = "https://cbuild.kusto.windows.net";

        public static string DefaultAzureTenantId { get; } = "72f988bf-86f1-41af-91ab-2d7cd011db47";

        public static string DefaultAzureAppId { get; } = "22cabbbb-1f32-4057-b601-225bab98348d";

        public static IReadOnlyDictionary<CloudBuildEnvironment, EnvironmentConfiguration> DefaultEnvironments { get; } =
            new Dictionary<CloudBuildEnvironment, EnvironmentConfiguration>
            {
                {
                    CloudBuildEnvironment.Production,
                    new EnvironmentConfiguration
                    {
                        KustoDatabaseName = "CloudBuildProd",
                        AzureSubscriptionName = "CloudBuild-PROD",
                        AzureSubscriptionId = "7965fc55-7602-4cf6-abe4-e081cf119567",
                        AzureKeyVaultSubscriptionId = "41cf5fb3-558b-467d-b6cd-dd7e6c18945d",
                    }
                },
                {
                    CloudBuildEnvironment.Test,
                    new EnvironmentConfiguration
                    {
                        KustoDatabaseName = "CloudBuildCBTest",
                        AzureSubscriptionName = "CloudBuild_Test",
                        AzureSubscriptionId = "bf933bbb-8131-491c-81d9-26d7b6f327fa",
                        AzureKeyVaultSubscriptionId = "30c83465-21e5-4a97-9df2-d8dd19881d24",
                    }
                },
                {
                    CloudBuildEnvironment.ContinuousIntegration,
                    new EnvironmentConfiguration
                    {
                        KustoDatabaseName = "CloudBuildCI",
                        AzureSubscriptionName = "CloudBuild_Test",
                        AzureSubscriptionId = "bf933bbb-8131-491c-81d9-26d7b6f327fa",
                        AzureKeyVaultSubscriptionId = "bf933bbb-8131-491c-81d9-26d7b6f327fa",
                    }
                },
            };

        public static TimeSpan RedisScaleTimePerShard { get; } = TimeSpan.FromMinutes(20);
    }
}
