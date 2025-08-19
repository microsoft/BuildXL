// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using BuildXL.Engine.Tracing;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;

#if NETCOREAPP
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Routing;
#endif

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// Grpc server used by both orchestrator and workers
    /// </summary>
    public abstract class GrpcServer : IServer
    {
        private readonly LoggingContext m_loggingContext;
        private readonly DistributedInvocationId m_invocationId;

        private Server m_server;

        private readonly CancellationTokenSource m_cancellationSource = new CancellationTokenSource();

#if NETCOREAPP
        private Microsoft.Extensions.Hosting.IHost m_kestrelServer;
#endif

        // Expose the port to unit tests
        internal int? Port => m_server?.Ports.FirstOrDefault()?.BoundPort;

        /// <summary>
        /// Class constructor
        /// </summary>
        internal GrpcServer(LoggingContext loggingContext, DistributedInvocationId invocationId)
        {
            m_loggingContext = loggingContext;
            m_invocationId = invocationId;
        }

        /// <nodoc/>
        public abstract void Start(int port);

        /// <nodoc/>
        internal void Start(int port, ServerServiceDefinition serverService)
        {
            var interceptor = new ServerInterceptor(m_loggingContext, m_invocationId);

            ServerCredentials serverCreds = null;

            if (GrpcSettings.EncryptionEnabled)
            {
                string certSubjectName = GrpcSettings.CertificateSubjectName;
                if (GrpcEncryptionUtils.TryGetPublicAndPrivateKeys(certSubjectName, out string publicCertificate, out string privateKey, out var _, out string errorMessage) &&
                    publicCertificate != null &&
                    privateKey != null)
                {
                    serverCreds = new SslServerCredentials(
                        new List<KeyCertificatePair> { new KeyCertificatePair(publicCertificate, privateKey) },
                        null,
                        SslClientCertificateRequestType.RequestAndRequireButDontVerify);

                    Logger.Log.GrpcServerTrace(m_loggingContext, $"Server auth is enabled: {certSubjectName}");
                }
                else
                {
                    Logger.Log.GrpcServerTraceWarning(m_loggingContext, $"Could not extract public certificate and private key from '{certSubjectName}'. Server will be started without ssl. Error message: '{errorMessage}'");
                }
            }

            m_server = new Server(ClientConnectionManager.ServerChannelOptions)
            {
                Services = { serverService.Intercept(interceptor) },
                Ports = { new ServerPort(IPAddress.Any.ToString(), port, serverCreds ?? ServerCredentials.Insecure) },
            };

            m_server.Start();
        }

#if NETCOREAPP
        /// <summary>
        /// Running a kestrel server instead of grpc.core server if it is enabled.
        /// </summary>
        public Task StartKestrel(int port, Action<IServiceCollection> configureGrpcServices, Action<IEndpointRouteBuilder> configureEndpointRouteBuilder)
        {
            Logger.Log.GrpcServerTrace(m_loggingContext, $"Starting Kestrel server on port {port} with encryption enabled: {GrpcSettings.EncryptionEnabled}");
            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        // Remove the default (30MB) max request body limit so large unary gRPC messages are not rejected
                        options.Limits.MaxRequestBodySize = null; // unlimited
                        options.Listen(IPAddress.Any, port, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http2;
                            
                            // Configure HTTPS with proper certificate handling
                            if (GrpcSettings.EncryptionEnabled)
                            {
                                string certSubjectName = GrpcSettings.CertificateSubjectName;
                                if (GrpcEncryptionUtils.TryGetPublicAndPrivateKeys(certSubjectName, 
                                    out string publicCertificate, 
                                    out string privateKey, 
                                    out var _, 
                                    out string errorMessage) &&
                                    publicCertificate != null &&
                                    privateKey != null)
                                {
                                    // Configure HTTPS with the certificate
                                    listenOptions.UseHttps(httpsOptions =>
                                    {
                                        // Create X509Certificate2 from PEM strings
                                        var cert = X509Certificate2.CreateFromPem(publicCertificate, privateKey);
                                        
                                        // For Windows, we need to ensure the private key is properly associated
                                        // This is especially important for ephemeral certificates
                                        if (OperatingSystem.IsWindows())
                                        {
                                            // Create a new certificate with the private key properly stored
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
                                        
                                        httpsOptions.ServerCertificate = cert;
                                        
                                        // Require client certificates but don't verify them (equivalent to RequestAndRequireButDontVerify)
                                        httpsOptions.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;
                                        httpsOptions.ClientCertificateValidation = (certificate, chain, policyErrors) =>
                                        {
                                            // Always return true to skip validation, matching the legacy server behavior
                                            return true;
                                        };
                                    });
                                    
                                    Logger.Log.GrpcServerTrace(m_loggingContext, $"Kestrel HTTPS enabled with certificate: {certSubjectName}");
                                }
                                else
                                {
                                    Logger.Log.GrpcServerTraceWarning(m_loggingContext, $"Could not configure HTTPS for Kestrel. Certificate error: '{errorMessage}'. Server will start without HTTPS.");
                                    // Don't call UseHttps() if certificate is not available
                                }
                            }
                        });
                    });

                    webBuilder.ConfigureServices(services =>
                    {
                        // Register the interceptor so we get the same logging/tracing behavior as the legacy Grpc.Core path (serverService.Intercept(interceptor))
                        services.AddSingleton(new ServerInterceptor(m_loggingContext, m_invocationId));

                        // Configure gRPC with unlimited message sizes to match legacy server and add interceptor
                        services.AddGrpc(options =>
                        {
                            options.MaxReceiveMessageSize = int.MaxValue;
                            options.MaxSendMessageSize = int.MaxValue;
                            options.Interceptors.Add<ServerInterceptor>();
                            options.EnableDetailedErrors = EngineEnvironmentSettings.GrpcKestrelEnableDetailedErrors;
                        });

                        // Allow caller to register service implementations / additional services
                        configureGrpcServices(services);
                    });

                    webBuilder.ConfigureLogging(loggingBuilder => loggingBuilder.ClearProviders());

                    webBuilder.Configure((IApplicationBuilder app) =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(configureEndpointRouteBuilder);
                    });
                });

            m_kestrelServer = hostBuilder.Build();
            return m_kestrelServer.RunAsync(m_cancellationSource.Token);
        }
#endif

        /// <inheritdoc />
        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

        /// <inheritdoc />
        public Task DisposeAsync() => ShutdownAsync();

        /// <nodoc />
        public async Task ShutdownAsync()
        {
            if (m_server != null)
            {
                try
                {
                    await m_server.ShutdownAsync();
                }
                catch (InvalidOperationException)
                {
                    // Shutdown was already requested
                }
            }

#if NETCOREAPP
            if (m_kestrelServer != null)
            {
                try
                {
                    // Use StopAsync for proper programmatic shutdown
                    await m_kestrelServer.StopAsync(TimeSpan.FromSeconds(10));
                    
                    // Dispose the host to clean up resources
                    m_kestrelServer.Dispose();
                    m_kestrelServer = null;
                }
                catch (ObjectDisposedException)
                {
                    // Expected during disposal - host was already disposed
                    Logger.Log.GrpcServerTrace(m_loggingContext, "Kestrel server was already disposed during shutdown");
                }
                catch (OperationCanceledException)
                {
                    // Expected when shutdown times out
                    Logger.Log.GrpcServerTrace(m_loggingContext, "Kestrel shutdown was cancelled or timed out");
                }
                catch (Exception ex)
                {
                    Logger.Log.GrpcServerTraceWarning(m_loggingContext, $"Exception during Kestrel shutdown: {ex}");
                }
                finally
                {
                    // Cancel the token only after stopping the host
                    if (!m_cancellationSource.IsCancellationRequested)
                    {
                        await m_cancellationSource.CancelTokenAsyncIfSupported();
                    }
                }
            }
#endif
        }
    }
}