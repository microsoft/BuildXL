// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BuildXL.Engine.Distribution
{
    internal class ServerInterceptor : Interceptor
    {
        private readonly LoggingContext m_loggingContext;
        private readonly DistributedInvocationId m_invocationId;
        private const string ReceivedLogFormat = "[{0} -> SELF] {1} Received: {2}";
        private const string RespondedLogFormat = "[{0} -> SELF] {1} Responded: {2}. DurationMs: {3}";
        private readonly string m_token;
        private readonly bool m_encryptionEnabled; 

        public ServerInterceptor(LoggingContext loggingContext, DistributedInvocationId invocationId)
        {
            m_loggingContext = loggingContext;
            m_invocationId = invocationId;
            m_encryptionEnabled = EngineEnvironmentSettings.GrpcEncryptionEnabled;

            if (m_encryptionEnabled)
            {
                m_token = GrpcEncryptionUtil.TryGetTokenBuildIdentityToken(EngineEnvironmentSettings.CBBuildIdentityTokenPath);
            }
        }

        private (string Sender, string TraceId) InterceptCallContext(ServerCallContext context)
        {
            string method = context.Method;

            GrpcSettings.ParseHeader(context.RequestHeaders, out string sender, out var senderInvocationId, out string traceId, out string token);

            if (m_invocationId != senderInvocationId)
            {
                string failureMessage = $"The receiver and sender distributed invocation ids do not match. Receiver invocation id: {m_invocationId}. Sender invocation id: {senderInvocationId}.";
                Logger.Log.GrpcTrace(m_loggingContext, failureMessage);

                var trailers = new Metadata
                {
                    { GrpcMetadata.InvocationIdMismatch, GrpcMetadata.True },
                    { GrpcMetadata.IsUnrecoverableError, IsUnrecoverableMismatch(senderInvocationId) ? GrpcMetadata.True : GrpcMetadata.False }
                };

                throw new RpcException(
                    new Status(
                        StatusCode.InvalidArgument,
                        failureMessage),
                    trailers);
            }

            if (m_encryptionEnabled && token != m_token)
            {
                Logger.Log.GrpcAuthTrace(m_loggingContext, $"Authentication tokens do not match:\r\nReceived:{token}\r\nExpected:{m_token}");

                var trailers = new Metadata
                {
                    { GrpcMetadata.IsUnrecoverableError, GrpcMetadata.True }
                };

                throw new RpcException(new Status(StatusCode.Unauthenticated, "Call could not be authenticated."), trailers);
            }

            // example: [MW1AAP45DD9145A::89 -> SELF] 740adbf7-ae68-4a94-b6c1-578b4a2ecb67 Received: /BuildXL.Distribution.Grpc.Orchestrator/Notify.
            Logger.Log.GrpcTrace(m_loggingContext, string.Format(ReceivedLogFormat, sender, traceId, method));
            return (sender, traceId);
        }

        private bool IsUnrecoverableMismatch(DistributedInvocationId senderInvocationId)
        {
            // If the build ids match, we don't want to signal an unrecoverable state
            return senderInvocationId.RelatedActivityId != m_invocationId.RelatedActivityId;
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            (var sender, var traceId) = InterceptCallContext(context);
            var watch = Stopwatch.StartNew();
            var result = await continuation(request, context);
            Logger.Log.GrpcTrace(m_loggingContext, string.Format(RespondedLogFormat, sender, traceId, context.Method, watch.ElapsedMilliseconds));
            return result;
        }

        public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
        {
            (var sender, var traceId) = InterceptCallContext(context);
            var watch = Stopwatch.StartNew();
            var result = await continuation(requestStream, context);
            Logger.Log.GrpcTrace(m_loggingContext, string.Format(RespondedLogFormat, sender, traceId, watch.ElapsedMilliseconds));
            return result;
        }

        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            (var sender, var traceId) = InterceptCallContext(context);
            var watch = Stopwatch.StartNew();
            await continuation(request, responseStream, context);
            Logger.Log.GrpcTrace(m_loggingContext, string.Format(RespondedLogFormat, sender, traceId, watch.ElapsedMilliseconds));
        }

        public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
        {
            (var sender, var traceId) = InterceptCallContext(context);
            var watch = Stopwatch.StartNew();
            await continuation(requestStream, responseStream, context);
            Logger.Log.GrpcTrace(m_loggingContext, string.Format(RespondedLogFormat, sender, traceId, watch.ElapsedMilliseconds));
        }
    }
}