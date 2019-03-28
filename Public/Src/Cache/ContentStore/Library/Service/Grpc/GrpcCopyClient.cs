// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStore.Grpc; // Can't rename ProtoBuf
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// An implementation of a CAS copy helper client based on GRPC.
    /// TODO: Consolidate with GrpcClient to deduplicate code. (bug 1365340)
    /// </summary>
    public class GrpcCopyClient : IShutdown<BoolResult>
    {
        private readonly Channel _channel;
        private readonly ContentServer.ContentServerClient _client;

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcCopyClient" /> class.
        /// </summary>
        public GrpcCopyClient(Channel channel)
        {
            GrpcEnvironment.InitializeIfNeeded();
            _client = new ContentServer.ContentServerClient(channel);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcCopyClient" /> class.
        /// </summary>
        public GrpcCopyClient(string host, int grpcPort)
        {
            GrpcEnvironment.InitializeIfNeeded();
            _channel = new Channel(host, grpcPort, ChannelCredentials.Insecure);
            _client = new ContentServer.ContentServerClient(_channel);
        }

        /// <inheritdoc />
        public async Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;

            if (_channel != null)
            {
                await _channel.ShutdownAsync();
            }

            ShutdownCompleted = true;
            return BoolResult.Success;
        }

        /// <summary>
        /// Copies file to stream
        /// </summary>
        public async Task<CopyFileResult> CopyFileAsync(Context context, ContentHash contentHash, AbsolutePath destinationPath, long fileSize = -1, bool enableCompression = false)
        {
            // TODO: Pipe through flag for compression type
            CopyCompression compression = enableCompression ? CopyCompression.Gzip : CopyCompression.None;
            AsyncServerStreamingCall<CopyFileResponse> response = _client.CopyFile(new CopyFileRequest()
            {
                TraceId = context.Id.ToString(),
                HashType = (int)contentHash.HashType,
                ContentHash = contentHash.ToByteString(),
                Drive = destinationPath.DriveLetter.ToString(),
                Offset = 0,
                Compression = compression
            });

            IAsyncStreamReader<CopyFileResponse> replyStream = response.ResponseStream;
            long bytesReceived = 0L;

            using (var stream = new
                FileStream(destinationPath.Path, FileMode.Create, FileAccess.Write, FileShare.None, ContentStore.Grpc.Utils.DefaultBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                bytesReceived = await StreamContentAsync(stream, response.ResponseStream);

                while (await replyStream.MoveNext(CancellationToken.None))
                {
                    CopyFileResponse oneOfManyReply = replyStream.Current;
                    bytesReceived += oneOfManyReply.Content.Length;
                    oneOfManyReply.Content.WriteTo(stream);
                }
            }

            if (fileSize >= 0 && bytesReceived != fileSize)
            {
                return new CopyFileResult(CopyFileResult.ResultCode.InvalidHash, $"Received {bytesReceived} bytes for {contentHash}, expected {fileSize}");
            }
            return CopyFileResult.Success;
        }

        private async Task<long> StreamContentAsync(Stream fileStream, IAsyncStreamReader<CopyFileResponse> replyStream)
        {
            long bytesReceived = 0L;
            while (await replyStream.MoveNext(CancellationToken.None))
            {
                CopyFileResponse oneOfManyReply = replyStream.Current;
                bytesReceived += oneOfManyReply.Content.Length;
                oneOfManyReply.Content.WriteTo(fileStream);
            }
            return bytesReceived;
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
                    throw new ClientCanRetryException(context, $"{nameof(GrpcCopyClient)} failed to detect running service");
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
