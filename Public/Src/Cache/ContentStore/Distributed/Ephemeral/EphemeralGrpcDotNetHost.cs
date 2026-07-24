// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
#if NETCOREAPP
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoBuf.Grpc.Server;
using AspNetHost = Microsoft.Extensions.Hosting.Host;
#endif

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral
{
    /// <summary>
    /// Configuration for <see cref="EphemeralGrpcDotNetHost"/>. Mirrors the shape of <see cref="GrpcCoreServerHostConfiguration"/>
    /// so both hosts are interchangeable — either or both ports may be set; when <see cref="EncryptedGrpcPort"/> is set the
    /// host loads the certificate identified by <see cref="GrpcEncryptionUtils.GetChannelEncryptionOptions"/>.
    /// </summary>
    public record EphemeralGrpcDotNetHostConfiguration(
        int? GrpcPort = null,
        int? EncryptedGrpcPort = null,
        GrpcDotNetServerOptions? GrpcOptions = null);

    /// <summary>
    /// Grpc.Net (Kestrel + Grpc.AspNetCore) implementation of <see cref="IGrpcServerHost{TConfiguration}"/> for the
    /// ephemeral cache. Supports the same unencrypted / TLS listener modes as <see cref="GrpcCoreServerHost"/>.
    /// </summary>
    public sealed class EphemeralGrpcDotNetHost : IGrpcServerHost<EphemeralGrpcDotNetHostConfiguration>
    {
        private static readonly Tracer Tracer = new(nameof(EphemeralGrpcDotNetHost));
#if NETCOREAPP
        private IHost? _webHost;
#endif

        /// <inheritdoc />
        public async Task<BoolResult> StartAsync(OperationContext context, EphemeralGrpcDotNetHostConfiguration configuration, IEnumerable<IGrpcServiceEndpoint> grpcEndpoints)
        {
            Contract.Assert(configuration.GrpcPort != configuration.EncryptedGrpcPort, "GrpcPort and EncryptedGrpcPort cannot be the same");

            var hasPlain = configuration.GrpcPort is > 0;
            var hasEncrypted = configuration.EncryptedGrpcPort is > 0;
            if (!hasPlain && !hasEncrypted)
            {
                return new BoolResult("No gRPC ports were configured for the server.");
            }

            Tracer.Debug(context, $"Initializing gRPC.NET environment for ephemeral cache. GrpcPort={configuration.GrpcPort}, EncryptedGrpcPort={configuration.EncryptedGrpcPort}.");
#if NETCOREAPP
            var grpcOptions = configuration.GrpcOptions ?? GrpcDotNetServerOptions.Default;

            X509Certificate2? serverCertificate = null;
            if (hasEncrypted)
            {
                var certificateLoad = TryLoadServerCertificate(context, out serverCertificate, out var certLoadError);
                if (!certificateLoad)
                {
                    if (!hasPlain)
                    {
                        return new BoolResult($"Failed to load TLS certificate for encrypted gRPC port: {certLoadError}");
                    }

                    Tracer.Warning(context, $"Falling back to unencrypted-only listener. TLS certificate load failed: {certLoadError}");
                    hasEncrypted = false;
                }
            }

            var hostResult = await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var webHostBuilder = AspNetHost.CreateDefaultBuilder()
                        .ConfigureAppConfiguration((_, config) =>
                        {
                            // Mirrors BuildXL's distribution GrpcServer: disable ReloadOnChange on file-based
                            // configuration sources to avoid potential deadlocks during file watcher setup.
                            foreach (var source in config.Sources)
                            {
                                if (source is FileConfigurationSource fileSource)
                                {
                                    fileSource.ReloadOnChange = false;
                                }
                            }
                        })
                        .ConfigureWebHostDefaults(
                            webBuilder =>
                            {
                                webBuilder.ConfigureLogging(l => l.ClearProviders());

                                webBuilder.ConfigureKestrel(
                                    o =>
                                    {
                                        // Kestrel Limits tuning mirrors BuildXL.Engine.Distribution.Grpc.GrpcServer.StartKestrel.
                                        // These non-default values have been tuned in production for the distribution gRPC server:
                                        //  * MaxRequestBodySize=null: default 30 MB rejects large unary gRPC payloads.
                                        //  * Http2 KeepAlive: Grpc.Net.Client uses PooledConnectionIdleTimeout=Infinite, so
                                        //    without disabling server-initiated pings and extending the ping timeout Kestrel
                                        //    closes idle connections and clients see "socket is in a bad state".
                                        //  * Min*DataRate=null: prevents Kestrel from killing slow but valid large streams.
                                        o.Limits.MaxRequestBodySize = null;
                                        o.Limits.Http2.KeepAlivePingDelay = TimeSpan.MaxValue;
                                        o.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(20);
                                        o.Limits.MinRequestBodyDataRate = null;
                                        o.Limits.MinResponseDataRate = null;

                                        if (hasPlain)
                                        {
                                            o.Listen(IPAddress.Any, configuration.GrpcPort!.Value, listenOptions =>
                                            {
                                                listenOptions.Protocols = HttpProtocols.Http2;
                                            });
                                        }

                                        if (hasEncrypted)
                                        {
                                            o.Listen(IPAddress.Any, configuration.EncryptedGrpcPort!.Value, listenOptions =>
                                            {
                                                listenOptions.Protocols = HttpProtocols.Http2;
                                                listenOptions.UseHttps(httpsOptions =>
                                                {
                                                    httpsOptions.ServerCertificate = serverCertificate;
                                                    // Matches the legacy Grpc.Core behavior (RequestAndRequireButDontVerify).
                                                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                                                    httpsOptions.ClientCertificateValidation = (_, _, _) => true;
                                                });
                                            });
                                        }
                                    });

                                webBuilder.Configure(
                                    app =>
                                    {
                                        app.UseRouting();
                                        app.UseEndpoints(
                                            endpoints =>
                                            {
                                                var endpointsAdapter = new GrpcEndpointCollectionAdapter(endpoints);
                                                foreach (var grpcEndpoint in grpcEndpoints)
                                                {
                                                    grpcEndpoint.MapServices(endpointsAdapter);
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
                            foreach (var grpcEndpoint in grpcEndpoints)
                            {
                                grpcEndpoint.AddServices(grpcServiceCollection);
                            }

                            // Ephemeral-cache endpoints use protobuf-net code-first bindings.
                            services.AddSingleton(MetadataServiceSerializer.BinderConfiguration);
                            services.AddCodeFirstGrpc();
                        });

                    var webHost = webHostBuilder.Build();
                    try
                    {
                        await webHost.StartAsync(context.Token);
                    }
                    catch
                    {
                        webHost.Dispose();
                        throw;
                    }

                    return Result.Success(webHost);
                });

            if (hostResult.Succeeded)
            {
                Contract.Assert(_webHost is null, "EphemeralGrpcDotNetHost.StartAsync called twice");
                _webHost = hostResult.Value;
                return BoolResult.Success;
            }

            return new BoolResult(hostResult);
#else
            await Task.Yield();
            return new BoolResult("Grpc.Net-backed ephemeral cache host is only supported on .NET Core targets.");
#endif
        }

        /// <inheritdoc />
        public Task<BoolResult> StopAsync(OperationContext context, EphemeralGrpcDotNetHostConfiguration configuration)
        {
#if NETCOREAPP
            Tracer.Debug(context, $"Shutting down gRPC.NET environment for ephemeral cache. GrpcPort={configuration.GrpcPort}, EncryptedGrpcPort={configuration.EncryptedGrpcPort}.");
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var webHost = _webHost;
                    if (webHost is null)
                    {
                        return BoolResult.Success;
                    }

                    _webHost = null;
                    // Mirrors BuildXL.Engine.Distribution.Grpc.GrpcServer: bound the stop by a timeout and
                    // tolerate ObjectDisposedException / OperationCanceledException, which are expected when
                    // the host has already begun teardown or when the timeout fires.
                    try
                    {
                        try
                        {
                            await webHost.StopAsync(TimeSpan.FromSeconds(10));
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                    finally
                    {
                        webHost.Dispose();
                    }

                    return BoolResult.Success;
                });
#else
            return Task.FromResult(BoolResult.Success);
#endif
        }
#if NETCOREAPP
        private static bool TryLoadServerCertificate(OperationContext context, out X509Certificate2? certificate, out string? errorMessage)
        {
            certificate = null;
            errorMessage = null;

            try
            {
                var encryptionOptions = GrpcEncryptionUtils.GetChannelEncryptionOptions();
                if (!GrpcEncryptionUtils.TryGetPublicAndPrivateKeys(
                        encryptionOptions.CertificateSubjectName,
                        out var publicCertificate,
                        out var privateKey,
                        out _,
                        out errorMessage)
                    || publicCertificate is null
                    || privateKey is null)
                {
                    return false;
                }

                var cert = X509Certificate2.CreateFromPem(publicCertificate, privateKey);

                // On Windows, re-import via PFX so the private key is properly associated with the cert instance.
                if (OperatingSystem.IsWindows())
                {
                    using (var tempCert = cert)
                    {
                        var pfxData = tempCert.Export(X509ContentType.Pfx);
#if NET9_0_OR_GREATER
                        cert = X509CertificateLoader.LoadPkcs12(pfxData, password: null);
#else
                        cert = new X509Certificate2(pfxData);
#endif
                    }
                }

                certificate = cert;
                return true;
            }
            catch (Exception ex)
            {
                Tracer.Warning(context, ex, "Failed to load TLS certificate for ephemeral cache Grpc.Net server.");
                errorMessage = ex.Message;
                return false;
            }
        }

        private sealed record GrpcEndpointCollectionAdapter(IEndpointRouteBuilder Endpoints) : IGrpcServiceEndpointCollection
        {
            public void MapService<TService>() where TService : class
            {
                Endpoints.MapGrpcService<TService>();
            }
        }

        private sealed record ServiceCollectionAdapter(IServiceCollection Services) : IGrpcServiceCollection
        {
            public void AddService<TService>(TService service) where TService : class
            {
                Services.AddSingleton<TService>(service);
            }
        }
#endif
    }
}
