// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using Grpc.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using System;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// Worker service impl
    /// </summary>
    public sealed class GrpcWorker : Worker.WorkerBase
    {
        private readonly IWorkerService m_workerService;

        internal GrpcWorker(IWorkerService service)
        {
            m_workerService = service;
        }

        /// Note: The logic of service methods should be replicated in Test.BuildXL.Distribution.WorkerServerMock
        /// <inheritdoc/>
        public override Task<RpcResponse> Attach(BuildStartData message, ServerCallContext context)
        {
            GrpcSettings.ParseHeader(context.RequestHeaders, out string sender, out var _, out var _, out var _);

            m_workerService.Attach(message, sender);

            return GrpcUtils.EmptyResponseTask;
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> ExecutePips(PipBuildRequest message, ServerCallContext context)
        {
            m_workerService.ExecutePipsAsync(message).Forget();
            return GrpcUtils.EmptyResponseTask;
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> Heartbeat(RpcResponse message, ServerCallContext context)
        {
            return GrpcUtils.EmptyResponseTask;
        }

        /// <inheritdoc/>
#pragma warning disable 1998 // Disable the warning for "This async method lacks 'await'"
        public override async Task<RpcResponse> StreamExecutePips(IAsyncStreamReader<PipBuildRequest> requestStream, ServerCallContext context)
        {
#if NETCOREAPP
            await foreach (var message in requestStream.ReadAllAsync())
            {
                m_workerService.ExecutePipsAsync(message).Forget();
            }

            return GrpcUtils.EmptyResponse;
#else
            throw new NotImplementedException();
#endif
        }
#pragma warning restore 1998

        /// <inheritdoc/>
        public override Task<RpcResponse> Exit(BuildEndData message, ServerCallContext context)
        {
            var failure = string.IsNullOrEmpty(message.Failure) ? Optional<string>.Empty : message.Failure;
            m_workerService.ExitRequested(failure);
            return GrpcUtils.EmptyResponseTask;
        }
    }
}