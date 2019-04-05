// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
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
        /// <remarks>
        /// Default: 3 minutes
        /// </remarks>
        private static TimeSpan CallTimeout => EngineEnvironmentSettings.DistributionConnectTimeout;

        private static TimeSpan InactiveTimeout => EngineEnvironmentSettings.DistributionInactiveTimeout;

        private static int MaxRetry => GrpcConstants.MaxRetryForGrpcMessages; 

        internal readonly Channel Channel;
        private LoggingContext m_loggingContext;
        private string m_buildId;

        private string GenerateLog(string traceId, string status, uint numTry, string description)
        {
            // example: [SELF -> MW1AAP45DD9145A::89] e709c667-ef88-464c-8557-232b02463976 Call#1. Description 
            // example: [SELF -> MW1AAP45DD9145A::89] e709c667-ef88-464c-8557-232b02463976 Sent#1. Duration: Milliseconds 
            // example: [SELF -> MW1AAP45DD9145A::89] e709c667-ef88-464c-8557-232b02463976 Fail#1. Duration: Milliseconds. Failure: 
            return string.Format("[SELF -> {0}] {1} {2}#{3}. {4}", Channel.Target, traceId, status, numTry, description);
        }

        public ClientConnectionManager(LoggingContext loggingContext, string ipAddress, int port, string buildId)
        {
            m_buildId = buildId;
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
            string operation,
            CancellationToken cancellationToken = default(CancellationToken),
            bool waitForConnection = false)
        {
            var watch = Stopwatch.StartNew();

            TimeSpan waitForConnectionDuration = TimeSpan.Zero;
            TimeSpan totalCallDuration = TimeSpan.Zero;

            if (waitForConnection)
            {
                try
                {
                    Logger.Log.GrpcTrace(m_loggingContext, $"Attempt to connect to {Channel.Target}. ChannelState {Channel.State}. Operation {operation}");
                    await Channel.ConnectAsync(DateTime.UtcNow.Add(InactiveTimeout));
                    Logger.Log.GrpcTrace(m_loggingContext, $"Connected to {Channel.Target}. Duration {watch.ElapsedMilliseconds}ms");
                }
                catch (OperationCanceledException e)
                {
                    Logger.Log.GrpcTrace(m_loggingContext, $"Failed to connect to {Channel.Target}. Duration {watch.ElapsedMilliseconds}ms. Failure {e.Message}");
                    return new RpcCallResult<Unit>(RpcCallResultState.Cancelled, attempts: 1, duration: TimeSpan.Zero, waitForConnectionDuration: watch.Elapsed);
                }

                waitForConnectionDuration = watch.Elapsed;
            }

            Guid traceId = Guid.NewGuid();
            var headers = new Metadata();
            headers.Add(GrpcConstants.TraceIdKey, traceId.ToByteArray());
            headers.Add(GrpcConstants.BuildIdKey, m_buildId);

            RpcCallResultState state = RpcCallResultState.Succeeded;
            Failure failure = null;

            uint numTry = 0;
            while (numTry < MaxRetry)
            {
                numTry++;
                watch.Restart();

                try
                {
                    var callOptions = new CallOptions(
                        deadline: DateTime.UtcNow.Add(CallTimeout),
                        cancellationToken: cancellationToken,
                        headers: headers);

                    Logger.Log.GrpcTrace(m_loggingContext, GenerateLog(traceId.ToString(), "Call", numTry, operation));
                    await func(callOptions);
                    Logger.Log.GrpcTrace(m_loggingContext, GenerateLog(traceId.ToString(), "Sent", numTry, $"Duration: {watch.ElapsedMilliseconds}ms"));
                    break;
                }
                catch (RpcException e)
                {
                    state = e.Status.StatusCode == StatusCode.Cancelled ? RpcCallResultState.Cancelled : RpcCallResultState.Failed;

                    Logger.Log.GrpcTrace(m_loggingContext, GenerateLog(traceId.ToString(), "Fail", numTry, $"Duration: {watch.ElapsedMilliseconds}ms. Failure: {e.Message}"));

                    failure = state == RpcCallResultState.Failed ? new RecoverableExceptionFailure(new BuildXLException(e.Message, e)) : null;

                    // If the call is NOT cancelled, retry the call.
                    if (state == RpcCallResultState.Cancelled)
                    {
                        break;
                    }
                }
                finally
                {
                    totalCallDuration += watch.Elapsed;
                }
            }

            if (state == RpcCallResultState.Succeeded)
            {
                return new RpcCallResult<Unit>(Unit.Void, attempts: numTry, duration: totalCallDuration, waitForConnectionDuration: waitForConnectionDuration);
            }

            return new RpcCallResult<Unit>(
                state,
                attempts: numTry,
                duration: totalCallDuration,
                waitForConnectionDuration: waitForConnectionDuration,
                lastFailure: failure);
        }
    }
}