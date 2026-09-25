// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Grpc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IpcResult = BuildXL.Ipc.Common.IpcResult;

namespace BuildXL.Ipc.GrpcBasedIpc
{
    /// <summary>
    /// Implementation based on gRPC
    /// </summary>

    internal sealed class GrpcIpcServer : IpcServer.IpcServerBase, IServer
    {
        private IIpcOperationExecutor m_executor;
        private Server m_grpcCoreServer;
        private IHost m_grpcDotNetServer;
        private readonly int m_port;
        private int m_lastOperationId = -1;
        private readonly TaskSourceSlim<string> m_connectionString;

        private IIpcLogger Logger { get; }

        public GrpcIpcServer(int port, IServerConfig config)
        {
            Config = config;
            m_port = port;
            Logger = config.Logger ?? VoidLogger.Instance;
            m_connectionString = TaskSourceSlim.Create<string>();
        }

        public IServerConfig Config { get; }

        private bool UseGrpcDotNet => Config is ServerConfig { UseGrpcDotNet: true };

        public Task<string> ConnectionString => m_connectionString.Task;

        public Task Completion => m_completionSource.Task;

        private readonly TaskCompletionSource m_completionSource = new();

        public override async Task<Grpc.IpcResult> Message(Grpc.IpcOperation request, ServerCallContext context)
        {
            var ipcOperation = request.FromGrpc();
            
            ipcOperation.Timestamp.Daemon_AfterReceivedTime = DateTime.UtcNow;
            var operationId = Interlocked.Increment(ref m_lastOperationId);

            try
            {
                Logger.Verbose("Executing operation {0}", operationId);
                ipcOperation.Timestamp.Daemon_BeforeExecuteTime = DateTime.UtcNow;

                IIpcResult result;
                if (request.IsSynchronous)
                {
                    result = await m_executor.ExecuteAsync(operationId, ipcOperation);
                }
                else
                {
                    // Operation is carried out asynchronously and we return a success immediately.
                    Logger.Verbose("Operation {0} will be performed asynchronously and a success will be sent back to the client");

                    // Wrap in a task so the whole operation runs asynchronously and this yields immediately
                    Task.Run(() => m_executor.ExecuteAsync(operationId, ipcOperation))
                    .Forget(e =>
                    {
                        Logger.Verbose("An exception was logged in a fire-and-forget request. Details: {0}", e.ToStringDemystified());
                    });

                    result = IpcResult.Success();
                }

                Logger.Verbose("Finished operation {0}", operationId);

                return result.AsGrpc();
            }
            catch (Exception e)
            {
                Logger.Verbose("Error in operation {0}: {1}", operationId, e.ToStringDemystified());
                return new Grpc.IpcResult()
                {
                    ExitCode = Interfaces.IpcResultStatus.ExecutionError.AsGrpc(),
                    Payload = e.ToStringDemystified()
                };
            }
        }

        void IDisposable.Dispose()
        {
            Logger.Verbose("Disposing...");
            if ((m_grpcCoreServer is not null || m_grpcDotNetServer is not null) && !Completion.IsCompleted)
            {
                throw new IpcException(IpcException.IpcExceptionKind.DisposeBeforeCompletion);
            }
        }

        void IStoppable.RequestStop()
        {
            // Async stop will set the completion
            StopAsync().Forget();
        }

        private async Task StopAsync()
        {
            try
            {
                Logger.Verbose("[GrpcIpcServer] Stopping....");
                if (UseGrpcDotNet && m_grpcDotNetServer is not null)
                {
                    await m_grpcDotNetServer.StopAsync();
                    m_grpcDotNetServer.Dispose();
                }
                else if (m_grpcCoreServer is not null)
                {
                    await m_grpcCoreServer.ShutdownAsync();
                }
            }
            catch (InvalidOperationException)
            {
                // Shutdown was already requested
            }
            catch (Exception e)
            {
                // We don't fail on stop/shutdown but let's log the error.
                Logger.Error("[GrpcIpcServer] An exception occurred while stopping the service. Details: {0}", e.ToStringDemystified());
            }

            Logger.Verbose("[GrpcIpcServer] Stopped");
            m_completionSource.SetResult();
        }

        void IServer.Start(IIpcOperationExecutor executor)
        {
            Logger.Verbose("[GrpcIpcServer] START");
            if (m_executor is not null)
            {
                throw new IpcException(IpcException.IpcExceptionKind.MultiStart);
            }

            m_executor = executor;
            if (!UseGrpcDotNet)
            {
                var serviceDefinition = IpcServer.BindService(this);
                var channelOptions = new[]
                {
                    new ChannelOption(ChannelOptions.MaxSendMessageLength, -1),
                    new ChannelOption(ChannelOptions.MaxReceiveMessageLength, -1),
                };

                m_grpcCoreServer = new Server(channelOptions)
                {
                    Services = { serviceDefinition },
                    Ports = { new ServerPort(IPAddress.Loopback.ToString(), m_port, ServerCredentials.Insecure) },
                };
            }
            else
            {
                m_grpcDotNetServer = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureLogging(logging => logging.ClearProviders());
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.Limits.MaxRequestBodySize = null;
                        options.Listen(IPAddress.Loopback, m_port, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
                    });
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddSingleton(this);
                        services.AddGrpc(options =>
                        {
                            options.MaxSendMessageSize = int.MaxValue;
                            options.MaxReceiveMessageSize = int.MaxValue;
                        });
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapGrpcService<GrpcIpcServer>());
                    });
                })
                .Build();
            }

            try
            {
                if (UseGrpcDotNet)
                {
                    m_grpcDotNetServer.StartAsync().GetAwaiter().GetResult();
                    m_connectionString.TrySetResult(m_port.ToString());
                }
                else
                {
                    m_grpcCoreServer.Start();
                    m_connectionString.TrySetResult(m_grpcCoreServer.Ports.Single().BoundPort.ToString());
                }
            }
            catch (IOException e)
            {
                m_connectionString.TrySetException(e);
                throw;
            }
        }
    }
}

#endif