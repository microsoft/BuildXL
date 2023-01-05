// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.Monitor.Library.Client;

namespace BuildXL.Cache.Monitor.App
{
    public record EnvironmentResources(IKustoClient KustoQueryClient) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
            KustoQueryClient.Dispose();
        }
    }
}
