// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using Grpc.Core;
using Grpc.Core.Interceptors;
using System.Threading;
using System.Collections.Generic;
using BuildXL.Utilities.Configuration;
using BuildXL.Engine.Tracing;
#if NETCOREAPP
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
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
                string certSubjectName = EngineEnvironmentSettings.CBBuildUserCertificateName;
                if (GrpcEncryptionUtil.TryGetPublicAndPrivateKeys(certSubjectName, out string publicCertificate, out string privateKey, out var _) &&
                    publicCertificate != null &&
                    privateKey != null)
                {
                    serverCreds = new SslServerCredentials(
                        new List<KeyCertificatePair> { new KeyCertificatePair(publicCertificate, privateKey) },
                        null,
                        SslClientCertificateRequestType.DontRequest);

                    Logger.Log.GrpcAuthTrace(m_loggingContext, $"Server-side SSL credentials is enabled.");
                }
                else
                {
                    Logger.Log.GrpcAuthWarningTrace(m_loggingContext, $"Could not extract public certificate and private key from '{certSubjectName}'. Server will be started without ssl.");
                }
            }

            m_server = new Server(ClientConnectionManager.ServerChannelOptions)
            {
                Services = { serverService.Intercept(interceptor) },
                Ports = { new ServerPort(IPAddress.Any.ToString(), port, serverCreds ?? ServerCredentials.Insecure) },
            };

            m_server.Start();
        }

        /// <summary>
        /// Running a kestrel server instead of grpc.core server if it is enabled.
        /// </summary>
        /// <remarks>
        /// Grpc.Core will be deprecated in May 2022, and we should start using Kestrel earlier than that date.
        /// </remarks>
        public Task StartKestrel(int port, Action<object> configure)
        {
#if NETCOREAPP
            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.Listen(IPAddress.Any, port, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http2;
                            listenOptions.UseHttps();
                        });
                    });

                    webBuilder.ConfigureServices((IServiceCollection services) =>
                        services.AddGrpc());

                    webBuilder.ConfigureLogging(loggingBuilder => loggingBuilder.ClearProviders());

                    webBuilder.Configure((IApplicationBuilder app) =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(configure);
                    });
                });

            m_kestrelServer = hostBuilder.Build();
            return m_kestrelServer.RunAsync(m_cancellationSource.Token);
#else
            return Task.CompletedTask;
#endif
        }

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
                m_cancellationSource.Cancel();
                await m_kestrelServer.WaitForShutdownAsync();
            }
#endif
        }
    }
}