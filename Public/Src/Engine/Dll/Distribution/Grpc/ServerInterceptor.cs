// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BuildXL.Engine.Distribution
{
    internal class ServerInterceptor : Interceptor
    {
        private readonly LoggingContext m_loggingContext;
        private readonly string m_buildId;
        private const string ReceivedLogFormat = "[{0} -> SELF] {1} Received: {2}";
        private const string RespondedLogFormat = "[{0} -> SELF] {1} Responded: {2}. DurationMs: {3}";

        public ServerInterceptor(LoggingContext loggingContext, string buildId)
        {
            m_loggingContext = loggingContext;
            m_buildId = buildId;
        }

        private (string, string) InterceptCallContext(ServerCallContext context)
        {
            string method = context.Method;

            GrpcSettings.ParseHeader(context.RequestHeaders, out string sender, out string senderBuildId, out string traceId);

            if (!string.IsNullOrEmpty(senderBuildId) && senderBuildId != m_buildId)
            {
                string failureMessage = $"The receiver and sender build ids do not match. Receiver build id: {m_buildId}. Sender build id: {senderBuildId}.";
                Logger.Log.GrpcTrace(m_loggingContext, failureMessage);
                throw new RpcException(
                    new Status(
                        StatusCode.InvalidArgument,
                        failureMessage));
            }

            // example: [MW1AAP45DD9145A::89 -> SELF] 740adbf7-ae68-4a94-b6c1-578b4a2ecb67 Received: /BuildXL.Distribution.Grpc.Master/Notify.
            Logger.Log.GrpcTrace(m_loggingContext, string.Format(ReceivedLogFormat, sender, traceId, method));
            return (sender, traceId);
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            (string, string) tuple = InterceptCallContext(context);
            var watch = Stopwatch.StartNew();
            var result = await continuation(request, context);
            Logger.Log.GrpcTrace(m_loggingContext, string.Format(RespondedLogFormat, tuple.Item1, tuple.Item2, context.Method, watch.ElapsedMilliseconds));
            return result;
        }

        public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
        {
            (string, string) tuple = InterceptCallContext(context);
            var watch = Stopwatch.StartNew();
            var result = await continuation(requestStream, context);
            Logger.Log.GrpcTrace(m_loggingContext, string.Format(RespondedLogFormat, tuple.Item1, tuple.Item2, watch.ElapsedMilliseconds));
            return result;
        }

        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            (string, string) tuple = InterceptCallContext(context);
            var watch = Stopwatch.StartNew();
            await continuation(request, responseStream, context);
            Logger.Log.GrpcTrace(m_loggingContext, string.Format(RespondedLogFormat, tuple.Item1, tuple.Item2, watch.ElapsedMilliseconds));
        }

        public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
        {
            (string, string) tuple = InterceptCallContext(context);
            var watch = Stopwatch.StartNew();
            await continuation(requestStream, responseStream, context);
            Logger.Log.GrpcTrace(m_loggingContext, string.Format(RespondedLogFormat, tuple.Item1, tuple.Item2, watch.ElapsedMilliseconds));
        }
    }
}