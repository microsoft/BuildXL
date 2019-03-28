// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Grpc.Core;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <nodoc/>
    internal sealed class ClientConnectionManager
    {
        /// <summary>
        /// Default channel options for clients/servers to send/receive unlimited messages.
        /// </summary>
        public static ChannelOption[] DefaultChannelOptions = new ChannelOption[] { new ChannelOption(ChannelOptions.MaxSendMessageLength, -1), new ChannelOption(ChannelOptions.MaxReceiveMessageLength, -1) };

        /// <summary>
        /// Maximum time for a Grpc call (both master->worker and worker->master)
        /// </summary>
        private static TimeSpan CallTimeout => EngineEnvironmentSettings.DistributionConnectTimeout;

        private static TimeSpan InactiveTimeout => EngineEnvironmentSettings.DistributionInactiveTimeout;

        internal readonly Channel Channel;
        private LoggingContext m_loggingContext;

        public ClientConnectionManager(LoggingContext loggingContext, string ipAddress, int port)
        {
            m_loggingContext = loggingContext;
            Channel = new Channel(
                    ipAddress,
                    port,
                    ChannelCredentials.Insecure,
                    DefaultChannelOptions);
        }

        public void Close()
        {
            Channel.ShutdownAsync().GetAwaiter().GetResult();
        }

        public async Task<RpcCallResult<Unit>> CallAsync(
            Func<CallOptions, AsyncUnaryCall<RpcResponse>> func, 
            string operationName,
            CancellationToken cancellationToken = default(CancellationToken),
            bool waitForConnection = false)
        {
            TimeSpan waitForConnectionDuration = TimeSpan.Zero;
            if (waitForConnection)
            {
                var waitForConnectionSw = new StopwatchVar();
                using (waitForConnectionSw.Start())
                {
                    try
                    {
                        Logger.Log.DistributionTrace(m_loggingContext, $"Attempt to connect to '{Channel.Target}' for operation {operationName}.  ChannelState '{Channel.State}'");
                        await Channel.ConnectAsync(DateTime.UtcNow.Add(InactiveTimeout));
                        Logger.Log.DistributionTrace(m_loggingContext, $"Connected to '{Channel.Target}' for operation {operationName}. Duration {waitForConnectionSw.TotalElapsed.TotalMilliseconds}ms.");
                    }
                    catch (OperationCanceledException e)
                    {
                        var duration = waitForConnectionSw.TotalElapsed;
                        Logger.Log.DistributionTrace(m_loggingContext, $"Failed to connect to '{Channel.Target}' for operation {operationName}. Duration {duration.TotalMilliseconds}ms. Failure {e.Message}");
                        return new RpcCallResult<Unit>(RpcCallResultState.Cancelled, attempts: 1, duration: TimeSpan.Zero, waitForConnectionDuration: duration);
                    }
                }

                waitForConnectionDuration = waitForConnectionSw.TotalElapsed;
            }

            var callDurationSw = new StopwatchVar();
            using (callDurationSw.Start())
            {
                try
                {
                    var callOptions = new CallOptions(
                        deadline: DateTime.UtcNow.Add(CallTimeout),
                        cancellationToken: cancellationToken);

                    Logger.Log.DistributionTrace(m_loggingContext, $"Attempt to call '{Channel.Target}' for operation {operationName}. ChannelState '{Channel.State}'");
                    await func(callOptions);
                    Logger.Log.DistributionTrace(m_loggingContext, $"Called '{Channel.Target}' for operation {operationName}. Duration {callDurationSw.TotalElapsed.TotalMilliseconds}ms.");
                }
                catch (RpcException e)
                {
                    RpcCallResultState state = e.Status.StatusCode == StatusCode.Cancelled ? RpcCallResultState.Cancelled : RpcCallResultState.Failed;

                    Logger.Log.DistributionTrace(m_loggingContext, $"Failed to call '{Channel.Target}' for operation {operationName}. Duration {callDurationSw.TotalElapsed.TotalMilliseconds}ms. Failure {e.Message}");
                    return new RpcCallResult<Unit>(
                        state, 
                        attempts: 1, 
                        duration: callDurationSw.TotalElapsed, 
                        waitForConnectionDuration: waitForConnectionDuration, 
                        lastFailure: state == RpcCallResultState.Failed 
                            ? new RecoverableExceptionFailure(new BuildXLException(e.Message, e)) 
                            : null);
                }
            }

            return new RpcCallResult<Unit>(Unit.Void, attempts: 1, duration: callDurationSw.TotalElapsed, waitForConnectionDuration: waitForConnectionDuration);
        }
    }
}