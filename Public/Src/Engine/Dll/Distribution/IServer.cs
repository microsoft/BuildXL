// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
#if NETCOREAPP
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
#endif

namespace BuildXL.Engine.Distribution
{
    internal interface IServer : IDisposable
    {
        void Start(int port);
#if NETCOREAPP
        Task StartKestrel(int port, Action<IServiceCollection> configureGrpcServices, Action<IEndpointRouteBuilder> configureEndpointRouteBuilder);
#endif
        Task ShutdownAsync();
        Task DisposeAsync();
    }
}