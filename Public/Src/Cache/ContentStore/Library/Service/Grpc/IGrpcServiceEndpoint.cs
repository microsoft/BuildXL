// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Stores;
using static Grpc.Core.Server;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Exposes grpc services
    /// </summary>
    public interface IGrpcServiceEndpoint : IStartupShutdownSlim
    {
        /// <summary>
        /// Binds service methods for use in gRPC.Core server.
        /// </summary>
        /// <param name="services"></param>
        void BindServices(ServiceDefinitionCollection services);

        /// <summary>
        /// Used for gRPC.NET version. Maps service endpoint so it can be found when routing incoming requests.
        /// </summary>
        void MapServices(IGrpcServiceEndpointCollection endpoints);

        /// <summary>
        /// Used for gRPC.NET version. Exposes service in service collection.
        /// </summary>
        void AddServices(IGrpcServiceCollection services);
    }

    /// <summary>
    /// Allows adding a service to gRPC.Net core service collection.
    /// </summary>
    public interface IGrpcServiceCollection
    {
        void AddService<TService>(TService service) where TService : class;
    }

    /// <summary>
    /// Allows mapping a gRPC.NET service for use when routing
    /// </summary>
    public interface IGrpcServiceEndpointCollection
    {
        /// <summary>
        /// Maps the service endpoint so it can be found when routing incoming requests.
        /// </summary>
        void MapService<TService>() where TService : class;
    }
}
