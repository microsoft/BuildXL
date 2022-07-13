// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER

using System;
using System.Diagnostics.ContractsLight;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using static BuildXL.Ipc.Grpc.IpcServer;
using static BuildXL.Ipc.GrpcBasedIpc.SerializationExtensions;

namespace BuildXL.Ipc.GrpcBasedIpc
{
    /// <summary>
    /// Implementation based on gRPC.
    /// </summary>
    internal sealed class Client : IClient
   {
        private readonly static IpcResult s_clientStoppedResult = new(IpcResultStatus.TransmissionError, "Could not post IPC request: the client has already terminated.");

        private readonly PendingOperationTracker m_operationWrapper;
        private readonly GrpcChannel m_channel;
        private readonly IpcServerClient m_client;

        private IIpcLogger Logger { get; }

        /// <inheritdoc />
        public IClientConfig Config { get; }

        /// <nodoc />
        public int Port { get; }

        /// <inheritdoc />
        public Task Completion => m_operationWrapper.Completion;
        private readonly TaskCompletionSource m_clientCompletionSource = new TaskCompletionSource();

        /// <inheritdoc />
        public void RequestStop() => m_operationWrapper.RequestStop();

        /// <nodoc />
        internal Client(IClientConfig config, int port)
        {
            Contract.Requires(config != null);
            Config = config;
            Port = port;
            Logger = config.Logger ?? VoidLogger.Instance;

            var channelOptions = new GrpcChannelOptions
            {
                MaxSendMessageSize = int.MaxValue,
                MaxReceiveMessageSize = int.MaxValue,
                HttpHandler = new SocketsHttpHandler
                {
                    UseCookies = false,
                    Expect100ContinueTimeout = TimeSpan.Zero,
                    PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                    EnableMultipleHttp2Connections = true
                },
            };


            var defaultMethodConfig = new MethodConfig
            {
                Names = { MethodName.Default },
                RetryPolicy = new RetryPolicy
                {
                    MaxAttempts = Config.MaxConnectRetries + 1, // Attempts = Retries + 1
                    InitialBackoff = Config.ConnectRetryDelay,
                    MaxBackoff = TimeSpan.FromSeconds(10),
                    BackoffMultiplier = 1,
                    RetryableStatusCodes = {
                            StatusCode.Unavailable,
                            StatusCode.Internal,
                            StatusCode.Unknown }
                }
            };

            channelOptions.ServiceConfig = new ServiceConfig
            {
                MethodConfigs = { defaultMethodConfig },
                LoadBalancingConfigs = { new PickFirstConfig() },
            };

            m_channel = GrpcChannel.ForAddress($"http://{IPAddress.Loopback}:{Port}", channelOptions);
            m_client = new IpcServerClient(m_channel);

            m_operationWrapper = new PendingOperationTracker(name: $"IpcClient:{Port}",
                onStopAsync: () =>
                {
                    // Dispose is the recommended way to shut down a gRPC.NET client
                    Logger.Verbose("Client disposing gRPC channel...");
                    m_channel.Dispose();
                    Logger.Verbose("Client disposed gRPC channel");
                    return Task.CompletedTask;
                },
                logger: Logger);
        }

        /// <inheritdoc />
        void IDisposable.Dispose()
        {
            RequestStop();
            Completion.GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public Task<IIpcResult> Send(IIpcOperation operation)
        {
            operation.Timestamp.Request_BeforePostTime = DateTime.UtcNow;
            return m_operationWrapper.PerformOperationAsync<IIpcResult>(async () =>
            {
                try
                {
                    operation.Timestamp.Request_BeforeSendTime = DateTime.UtcNow;
                    var rpc = m_client.MessageAsync(operation.AsGrpc());
                    operation.Timestamp.Request_AfterSendTime = DateTime.UtcNow;

                    // Note: we await the RPC even if ShouldWaitForServerAck is false.
                    // Instead of fire-and-forgetting a gRPC in this case, it's on the server side that we fire-and-forget the operation
                    // and just return a success. In this sense the name is a bit unfortunate because we are precisely
                    // waiting for an ACK from the server (but the behavior is still the intended: the operation will be carried
                    // out asynchronously) - check GrpcIpcServer for the corresponding logic.
                    // We have to do it this way because if we just fire-forget it, the IPC Pip might be killed before the gRPC request is sent
                    // and the server will never get it (this was actually observed to happen and very consistently).
                    // TODO: Consider changing the 'ShouldWaitForserverAck' to 'IsSynchronous' if this implementation sticks.
                    var result = await rpc;
                        
                    operation.Timestamp.Request_AfterServerAckTime = DateTime.UtcNow;
                    return result.FromGrpc();
                }
                catch (RpcException e)
                {
                    if (e.StatusCode == StatusCode.Unavailable)
                    {
                        return new IpcResult(IpcResultStatus.ConnectionError, e.ToStringDemystified());
                    }

                    return new IpcResult(IpcResultStatus.TransmissionError, e.ToStringDemystified());
                }
                catch (Exception e)
                {
                    return new IpcResult(IpcResultStatus.GenericError, e.ToStringDemystified());
                }
            },

            afterStopResult: s_clientStoppedResult);
        }
    }
}

#endif