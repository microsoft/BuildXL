// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Core.Logging;

namespace BuildXL.Plugin
{
    internal sealed class PluginGrpcInterceptor : Interceptor
    {
        /// <nodoc />
        public ILogger Logger { get; }

        /// <nodoc />
        public PluginGrpcInterceptor(ILogger logger)
        {
            Logger = logger;
        }

        private void ParseMetadata(Metadata metadata, out string requestId)
        {
            requestId = metadata.First(kv => kv.Key.Equals(GrpcPluginSettings.PluginRequestId)).Value;
        }

        /// <nodoc />
        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        { 
            ParseMetadata(context.RequestHeaders, out string requestId);

            Stopwatch sw = Stopwatch.StartNew();
            ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads); // Log available threads for slowdown investigation
            Logger.Debug($"Received requestId:{requestId} for {context.Method} at {DateTime.UtcNow:HH:mm:ss.fff} (available worker threads: {workerThreads}, I/O threads: {completionPortThreads})");
            var result = await continuation(request, context);
            Logger.Debug($"Sent response for requestId:{requestId} at {DateTime.UtcNow:HH:mm:ss.fff} method: {context.Method}, process took {sw.ElapsedMilliseconds} ms");
            return result;
        }

        /// <nodoc />
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            ParseMetadata(context.Options.Headers, out string requestId);
            Logger.Debug($"Sending requestId:{requestId} for Method: {context.Method.Name} to Service: {context.Method.ServiceName}");
            return continuation(request, context);
        }
    }
}
