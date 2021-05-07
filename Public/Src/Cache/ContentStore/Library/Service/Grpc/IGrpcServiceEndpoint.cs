// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Utils;
using Grpc.Core;
using static Grpc.Core.Server;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Exposes grpc services
    /// </summary>
    public interface IGrpcServiceEndpoint : IStartupShutdownSlim
    {
        void BindServices(ServiceDefinitionCollection services);
    }
}
