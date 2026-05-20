// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BuildXL.Engine.Distribution
{
    internal interface IServer : IDisposable
    {
        void Start(int port);
        Task StartKestrel(int port, Action<IServiceCollection> configureGrpcServices, Action<IEndpointRouteBuilder> configureEndpointRouteBuilder);
        Task ShutdownAsync();
        Task DisposeAsync();
    }
}