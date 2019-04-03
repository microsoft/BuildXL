// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
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
    public sealed class GrpcCopyClient : IShutdown<BoolResult>
    {
        private readonly Channel _channel;
        private readonly ContentServer.ContentServerClient _client;
        private readonly string _host;
        private readonly int _grpcPort;

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcCopyClient" /> class.
        /// </summary>
        private GrpcCopyClient(Channel channel)
        {
            GrpcEnvironment.InitializeIfNeeded();
            _client = new ContentServer.ContentServerClient(channel);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcCopyClient" /> class.
        /// </summary>
        private GrpcCopyClient(string host, int grpcPort)
        {
            GrpcEnvironment.InitializeIfNeeded();
            _channel = new Channel(host, (int)grpcPort, ChannelCredentials.Insecure);
            _client = new ContentServer.ContentServerClient(_channel);
            _host = host;
            _grpcPort = grpcPort;
        }

        /// <summary>
        /// Use an existing GRPC client if possible, else create a new one.
        /// </summary>
        /// <param name="host">Name of the host for the server (e.g. 'localhost').</param>
        /// <param name="grpcPort">GRPC port on the server.</param>
        public static GrpcCopyClient Create(string host, int grpcPort)
        {
            // TODO: Add caching of GrpcCopyClient objects
            // TODO: Add case where _clientPool has exceeded some maximum count
            return new GrpcCopyClient(host, grpcPort);
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
        /// Copies file to local path
        /// </summary>
        public async Task<CopyFileResult> CopyFileAsync(Context context, ContentHash contentHash, AbsolutePath destinationPath, CancellationToken cancellationToken, long fileSize = -1, bool enableCompression = false)
        {
            try
            {
                using (var stream = new FileStream(destinationPath.Path, FileMode.Create, FileAccess.Write, FileShare.None, ContentStore.Grpc.Utils.DefaultBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    return await CopyToAsync(context, contentHash, stream, cancellationToken, fileSize, enableCompression);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                return new CopyFileResult(CopyFileResult.ResultCode.DestinationPathError, ex, $"Error copying to file {destinationPath.Path}");
            }
        }

        /// <summary>
        /// Copies file to stream
        /// </summary>
        public async Task<CopyFileResult> CopyToAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cancellationToken, long fileSize = -1, bool enableCompression = false)
        {
            // TODO: Pipe through flag for compression type
            CopyCompression compression = enableCompression ? CopyCompression.Gzip : CopyCompression.None;
            long bytesReceived = 0L;

            try
            {
                AsyncServerStreamingCall<CopyFileResponse> response = _client.CopyFile(new CopyFileRequest
                {
                    TraceId = context.Id.ToString(),
                    HashType = (int)contentHash.HashType,
                    ContentHash = contentHash.ToByteString(),
                    // TODO: If `Drive` is expected to be the drive of the file on the source machine, then this should have nothing to do with the destination's drive
                    Drive = "B",
                    Offset = 0,
                    Compression = compression
                });

                bytesReceived = await StreamContentAsync(stream, response.ResponseStream);
            }
            catch (RpcException r) when (r.StatusCode == StatusCode.Unavailable && r.Message.Contains("Connect Failed"))
            {
                return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, r, $"Failed to connect to server {_host} at port {_grpcPort}");
            }

            if (bytesReceived == 0)
            {
                return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, $"Received {bytesReceived} bytes for {contentHash}. Source file does not exist");
            }
            else if (fileSize >= 0 && bytesReceived != fileSize)
            {
                return new CopyFileResult(CopyFileResult.ResultCode.InvalidHash, $"Received {bytesReceived} bytes for {contentHash}, expected {fileSize}");
            }

            return CopyFileResult.SuccessWithSize(bytesReceived);
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
            // noop for now
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GrpcCopyClient client
                && _host == client._host
                && _grpcPort == client._grpcPort;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return BuildXL.Utilities.HashCodeHelper.Combine(_host.GetHashCode(), _grpcPort);
        }
    }
}
