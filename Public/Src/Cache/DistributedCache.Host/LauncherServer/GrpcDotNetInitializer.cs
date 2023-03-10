// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
#if NET6_0_OR_GREATER
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Grpc.Server;
#endif

using LoggingAdapter = BuildXL.Cache.Host.Service.LoggingAdapter;

#nullable enable

namespace BuildXL.Launcher.Server
{
    /// <summary>
    /// gRPC.NET infrastructure initializer.
    /// </summary>
    public class GrpcDotNetInitializer : ICacheServerGrpcHost
    {
        private static readonly Tracer _tracer = new Tracer(nameof(GrpcDotNetInitializer));
        private ConcurrentDictionary<LocalServerConfiguration, IHost> _webHosts = new ();

        /// <nodoc />
        public GrpcDotNetInitializer()
        {
        }
        
        /// <inheritdoc />
        public async Task<BoolResult> StartAsync(OperationContext context, LocalServerConfiguration configuration, ICacheServer cacheServer)
        {
            Contract.Requires(configuration.UseGrpcDotNet, "The gRPC.NET must be enabled in order to use this type.");
            int port = configuration.GrpcPort;

            _tracer.Debug(context, $"Initializing gRPC.NET environment. Port={port}.");
#if NET6_0_OR_GREATER
            var grpcOptions = configuration.GrpcDotNetServerOptions;
            var hostResult = await context.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    var webHostBuilder = Host.CreateDefaultBuilder()
                        .ConfigureWebHostDefaults(
                            webBuilder =>
                            {
                                webBuilder.ConfigureLogging(
                                    l =>
                                    {
                                        l.ClearProviders();

                                        if (grpcOptions.MinLogLevelVerbosity != null)
                                        {
                                            l.AddProvider(new LoggingAdapter("ASPNET", context, (LogLevel)grpcOptions.MinLogLevelVerbosity.Value));
                                        }
                                    });

                                webBuilder.ConfigureKestrel(
                                    o =>
                                    {
                                        o.ConfigureEndpointDefaults(
                                            listenOptions =>
                                            {
                                                listenOptions.Protocols = HttpProtocols.Http2;
                                            });


                                        o.ListenAnyIP(port);
                                    });

                                webBuilder.Configure(
                                    app =>
                                    {
                                        app.UseRouting();
                                        app.UseEndpoints(
                                            endpoints =>
                                            {
                                                var endpointsAdapter = new GrpcEndpointCollectionAdapter(endpoints);
                                                foreach (var endpoint in cacheServer.GrpcEndpoints)
                                                {
                                                    endpoint.MapServices(endpointsAdapter);
                                                }
                                            });
                                    });
                            });

                    webHostBuilder.ConfigureServices(
                        services =>
                        {
                            services.AddGrpc(
                                options =>
                                {
                                    options.MaxReceiveMessageSize = grpcOptions.MaxReceiveMessageSize;
                                    options.MaxSendMessageSize = grpcOptions.MaxSendMessageSize;
                                    options.EnableDetailedErrors = grpcOptions.EnableDetailedErrors;
                                    options.IgnoreUnknownServices = grpcOptions.IgnoreUnknownServices;
                                    options.ResponseCompressionAlgorithm = grpcOptions.ResponseCompressionAlgorithm;
                                    options.ResponseCompressionLevel = grpcOptions.ResponseCompressionLevel;
                                });

                            var grpcServiceCollection = new ServiceCollectionAdapter(services);

                            foreach (var grpcEndpoint in cacheServer.GrpcEndpoints)
                            {
                                grpcEndpoint.AddServices(grpcServiceCollection);
                            }

                            // Required for GCS because unlike the content servers, the GCS uses code first approach.
                            services.AddSingleton<BinderConfiguration>(MetadataServiceSerializer.BinderConfiguration);
                            services.AddCodeFirstGrpc();
                        });

                    var webHost = webHostBuilder.Build();

                    await webHost.StartAsync(context.Token);
                    return Result.Success(webHost);
                });

            if (hostResult.Succeeded)
            {
                var added = _webHosts.TryAdd(configuration, hostResult.Value);
                Contract.Assert(added);
            }

            return hostResult;
#else
            throw new NotSupportedException();
#endif
        }

        private record GrpcEndpointCollectionAdapter(IEndpointRouteBuilder Endpoints) : IGrpcServiceEndpointCollection
        {
            /// <inheritdoc />
            public void MapService<TService>() where TService : class
            {
                Endpoints.MapGrpcService<TService>();
            }
        }

        private record ServiceCollectionAdapter(IServiceCollection Services) : IGrpcServiceCollection
        {
            /// <inheritdoc />
            public void AddService<TService>(TService service) where TService : class
            {
                Services.AddSingleton<TService>(service);
            }
        }

        /// <inheritdoc />
        public Task<BoolResult> StopAsync(OperationContext context, LocalServerConfiguration configuration)
        {
            // Not passing cancellation token since it will already be signaled
#if NET6_0_OR_GREATER
            bool removed = _webHosts.TryRemove(configuration, out var webHost);
            Contract.Assert(removed);


            int port = configuration.GrpcPort;
            _tracer.Debug(context, $"Shutting down gRPC.NET environment. Port={port}.");
            return context.PerformOperationAsync(
                _tracer,
                async () =>
                {
                    // _webHost is not null if 'StartAsync' method is called.
                    Contract.Assert(webHost != null, $"{nameof(StartAsync)} method must be called before calling {nameof(StopAsync)}.");

                    await webHost.StopAsync();

                    webHost.Dispose();
                    return BoolResult.Success;
                });
#else
            throw new NotSupportedException();
#endif
        }
    }
}
