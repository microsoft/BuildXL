// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
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

        public ServerInterceptor(LoggingContext loggingContext, string buildId)
        {
            m_loggingContext = loggingContext;
            m_buildId = buildId;
        }

        private void InterceptCallContext(ServerCallContext context)
        {
            string sender = context.Host;
            string traceId = string.Empty;
            string method = context.Method;
            string senderBuildId = string.Empty;

            foreach (var kvp in context.RequestHeaders)
            {
                if (kvp.Key == GrpcConstants.TraceIdKey)
                {
                    traceId = new Guid(kvp.ValueBytes).ToString();
                }
                else if (kvp.Key == GrpcConstants.BuildIdKey)
                {
                    senderBuildId = kvp.Value;
                }
            }

            if (!string.IsNullOrEmpty(senderBuildId) && senderBuildId != m_buildId)
            {
                throw new RpcException(
                    new Status(
                        StatusCode.InvalidArgument, 
                        $"The receiver and sender build ids do not match. Receiver build id: {m_buildId}. Sender build id: {senderBuildId}."));
            }

            // example: [MW1AAP45DD9145A::89 -> SELF] 740adbf7-ae68-4a94-b6c1-578b4a2ecb67 Received: /BuildXL.Distribution.Grpc.Master/Notify.
            Logger.Log.GrpcTrace(m_loggingContext, string.Format("[{0} -> SELF] {1} Received: {2}", sender, traceId, method));
        }

        public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            InterceptCallContext(context);
            return continuation(request, context);
        }

        public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
        {
            InterceptCallContext(context);
            return continuation(requestStream, context);
        }

        public override Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            InterceptCallContext(context);
            return continuation(request, responseStream, context);
        }

        public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
        {
            InterceptCallContext(context);
            return continuation(requestStream, responseStream, context);
        }
    }
}