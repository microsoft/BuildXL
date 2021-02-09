// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.Monitor.Library.Client;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Monitor;
using Microsoft.Azure.Management.Redis.Fluent;

namespace BuildXL.Cache.Monitor.App
{
    public record EnvironmentResources(
        IAzure Azure,
        IMonitorManagementClient MonitorManagementClient,
        IReadOnlyDictionary<string, IRedisCache> RedisCaches,
        IKustoClient KustoQueryClient) : IDisposable
    {
        public void Dispose()
        {
            MonitorManagementClient.Dispose();
            KustoQueryClient.Dispose();
        }
    }
}
