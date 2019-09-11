// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStore.Grpc;
using Grpc.Core;
// Can't rename ProtoBuf

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// An implementation of a CAS repair handling client based on GRPC.
    /// TODO: Consolidate with GrpcClient to deduplicate code. (bug 1365340)
    /// </summary>
    public class GrpcRepairClient : IShutdown<BoolResult>
    {
        private readonly Channel _channel;
        private readonly ContentServer.ContentServerClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcRepairClient" /> class.
        /// </summary>
        public GrpcRepairClient(uint grpcPort)
        {
            GrpcEnvironment.InitializeIfNeeded();
            _channel = new Channel(GrpcEnvironment.Localhost, (int)grpcPort, ChannelCredentials.Insecure, GrpcEnvironment.DefaultConfiguration);
            _client = new ContentServer.ContentServerClient(_channel);
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <summary>
        /// Triggers removal of local location from the content tracker.
        /// </summary>
        public async Task<StructResult<long>> RemoveFromTrackerAsync(Context context)
        {
            RemoveFromTrackerResponse response = await RunClientActionAndThrowIfFailedAsync(context, async () => await _client.RemoveFromTrackerAsync(new RemoveFromTrackerRequest { TraceId = context.Id.ToString() }));

            if (response.Header.Succeeded)
            {
                return new StructResult<long>(response.FilesEvicted);
            }
            else
            {
                return new StructResult<long>(response.Header.ErrorMessage, response.Header.Diagnostics);
            }
        }

        /// <inheritdoc />
        public async Task<BoolResult> ShutdownAsync(Context context)
        {
            try
            {
                ShutdownStarted = true;
                await _channel.ShutdownAsync();
                ShutdownCompleted = true;
                return BoolResult.Success;
            }
            catch (Exception ex)
            {
                // Catching all exceptions, even ClientCanRetryExceptions, because the teardown steps aren't idempotent.
                // In the worst case, the shutdown call comes while the service is offline, the service never receives it,
                // and then the service times out the session 10 minutes later (by default).
                ShutdownCompleted = true;
                return new BoolResult(ex);
            }
        }

        private async Task<T> RunClientActionAndThrowIfFailedAsync<T>(Context context, Func<Task<T>> clientAction)
        {
            try
            {
                return await clientAction();
            }
            catch (RpcException ex)
            {
                if (ex.Status.StatusCode == StatusCode.Unavailable)
                {
                    throw new ClientCanRetryException(context, $"{nameof(GrpcRepairClient)} failed to detect running service");
                }

                throw new ClientCanRetryException(context, ex.ToString(), ex);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
        }
    }
}
