// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.Monitor.Library.Client;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Monitor;

namespace BuildXL.Cache.Monitor.App
{
    public record EnvironmentResources(
        IAzure Azure,
        IMonitorManagementClient MonitorManagementClient,
        IKustoClient KustoQueryClient) : IDisposable
    {
        public void Dispose()
        {
            MonitorManagementClient.Dispose();
            KustoQueryClient.Dispose();
        }
    }
}
