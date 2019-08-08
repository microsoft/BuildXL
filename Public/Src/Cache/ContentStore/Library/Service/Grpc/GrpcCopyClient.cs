// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using ContentStore.Grpc;
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// An implementation of a CAS copy helper client based on GRPC.
    /// TODO: Consolidate with GrpcClient to deduplicate code. (bug 1365340)
    /// </summary>
    public sealed class GrpcCopyClient : StartupShutdownSlimBase
    {
        private readonly Channel _channel;
        private readonly ContentServer.ContentServerClient _client;
        private readonly int _bufferSize;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(GrpcCopyClient));

        internal GrpcCopyClientKey Key { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcCopyClient" /> class.
        /// </summary>
        internal GrpcCopyClient(GrpcCopyClientKey key, int? clientBufferSize = null)
        {
            GrpcEnvironment.InitializeIfNeeded();
            _channel = new Channel(key.Host, key.GrpcPort, ChannelCredentials.Insecure, GrpcEnvironment.DefaultConfiguration);
            _client = new ContentServer.ContentServerClient(_channel);
            _bufferSize = clientBufferSize ?? ContentStore.Grpc.CopyConstants.DefaultBufferSize;
            Key = key;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await _channel.ShutdownAsync();
            return BoolResult.Success;
        }

        /// <summary>
        /// Checks if file exists on remote machine.
        /// </summary>
        public async Task<FileExistenceResult> CheckFileExistsAsync(Context context, ContentHash hash)
        {
            try
            {
                ExistenceRequest request = new ExistenceRequest()
                {
                    TraceId = context.Id.ToString(),
                    HashType = (int)hash.HashType,
                    ContentHash = hash.ToByteString()
                };

                ExistenceResponse response = await _client.CheckFileExistsAsync(request);
                if (response.Header.Succeeded)
                {
                    return new FileExistenceResult();
                }
                else
                {
                    return new FileExistenceResult(FileExistenceResult.ResultCode.FileNotFound, response.Header.ErrorMessage);
                }
            }
            catch (RpcException r)
            {
                if (r.StatusCode == StatusCode.Unavailable)
                {
                    return new FileExistenceResult(FileExistenceResult.ResultCode.SourceError, r);
                }
                else
                {
                    return new FileExistenceResult(FileExistenceResult.ResultCode.Error, r);
                }
            }
        }

        /// <summary>
        /// Copies content from the server to the given local path.
        /// </summary>
        public Task<CopyFileResult> CopyFileAsync(Context context, ContentHash hash, AbsolutePath destinationPath, CancellationToken ct)
        {
            Func<Stream> streamFactory = () => new FileStream(destinationPath.Path, FileMode.Create, FileAccess.Write, FileShare.None, _bufferSize, FileOptions.SequentialScan);

            return CopyToAsync(context, hash, streamFactory, ct);
        }
        
        /// <summary>
        /// Copies content from the server to the given stream.
        /// </summary>
        public Task<CopyFileResult> CopyToAsync(Context context, ContentHash hash, Stream stream, CancellationToken ct)
        {
            return CopyToAsync(context, hash, () => stream, ct);
        }

        /// <summary>
        /// Copies content from the server to the stream returned by the factory.
        /// </summary>
        public async Task<CopyFileResult> CopyToAsync(Context context, ContentHash hash, Func<Stream> streamFactory, CancellationToken ct)
        {
            // Need to track shutdown to prevent invalid operation errors when the instance is used after it was shut down is called.
            using (var operationContext = TrackShutdown(context, ct))
            {
                return await CopyToCoreAsync(operationContext, hash, streamFactory);
            }
        }

        /// <summary>
        /// Copies content from the server to the stream returned by the factory.
        /// </summary>
        private async Task<CopyFileResult> CopyToCoreAsync(OperationContext context, ContentHash hash, Func<Stream> streamFactory)
        {
            try
            {
                CopyFileRequest request = new CopyFileRequest()
                {
                    TraceId = context.TracingContext.Id.ToString(),
                    HashType = (int)hash.HashType,
                    ContentHash = hash.ToByteString(),
                    Offset = 0,
                    Compression = Key.UseCompression ? CopyCompression.Gzip : CopyCompression.None
                };

                AsyncServerStreamingCall<CopyFileResponse> response = _client.CopyFile(request);

                Metadata headers = await response.ResponseHeadersAsync;

                // If the remote machine couldn't be contacted, GRPC returns an empty
                // header collection. GRPC would throw an RpcException when we tried
                // to stream response, but by that time we would have created target
                // stream. To avoid that, exit early instead.
                if (headers.Count == 0)
                {
                    return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, $"Failed to connect to copy server {Key.Host} at port {Key.GrpcPort}.");
                }

                // Parse header collection.
                string exception = null;
                string message = null;
                CopyCompression compression = CopyCompression.None;
                foreach(Metadata.Entry header in headers)
                {
                    switch (header.Key)
                    {
                        case "exception":
                            exception = header.Value;
                            break;
                        case "message":
                            message = header.Value;
                            break;
                        case "compression":
                            Enum.TryParse(header.Value, out compression);
                            break;
                    }
                }

                // Process reported server-side errors.
                if (exception != null)
                {
                    Contract.Assert(message != null);
                    switch (exception)
                    {
                        case "ContentNotFound":
                            return new CopyFileResult(CopyFileResult.ResultCode.FileNotFoundError, message);
                        default:
                            return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, message);
                    }
                }

                // We got headers back with no errors, so create the target stream.
                Stream targetStream;
                try
                {
                    targetStream = streamFactory();
                }
                catch (Exception targetException)
                {
                    return new CopyFileResult(CopyFileResult.ResultCode.DestinationPathError, targetException);
                }

                // Copy the content to the target stream.
                using (targetStream)
                {
                    switch (compression)
                    {
                        case CopyCompression.None:
                            await StreamContentAsync(targetStream, response.ResponseStream, context.Token);
                            break;
                        case CopyCompression.Gzip:
                            await StreamContentWithCompressionAsync(targetStream, response.ResponseStream, context.Token);
                            break;
                    }
                }

                return CopyFileResult.Success;
            }
            catch (RpcException r)
            {
                if (r.StatusCode == StatusCode.Unavailable)
                {
                    return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, r);
                }
                else
                {
                    return new CopyFileResult(CopyFileResult.ResultCode.Unknown, r);
                }
            }
        }

        /// <summary>
        /// Requests host to copy a file from another source machine.
        /// </summary>
        public async Task<BoolResult> RequestCopyFileAsync(Context context, ContentHash hash)
        {
            try
            {
                var request = new RequestCopyFileRequest
                {
                    TraceId = context.Id.ToString(),
                    ContentHash = hash.ToByteString(),
                    HashType = (int)hash.HashType
                };

                var response = await _client.RequestCopyFileAsync(request);

                return response.Header.Succeeded
                    ? BoolResult.Success
                    : new BoolResult(response.Header.ErrorMessage);
            }
            catch (RpcException r)
            {
                return new BoolResult(r);
            }
        }

        private async Task<(long Chunks, long Bytes)> StreamContentAsync(Stream targetStream, IAsyncStreamReader<CopyFileResponse> replyStream, CancellationToken ct)
        {
            Contract.Requires(targetStream != null);
            Contract.Requires(replyStream != null);

            long chunks = 0L;
            long bytes = 0L;
            while (await replyStream.MoveNext(ct))
            {
                chunks++;
                CopyFileResponse reply = replyStream.Current;
                bytes += reply.Content.Length;
                reply.Content.WriteTo(targetStream);
            }
            return (chunks, bytes);
        }

        private async Task<(long Chunks, long Bytes)> StreamContentWithCompressionAsync(Stream targetStream, IAsyncStreamReader<CopyFileResponse> replyStream, CancellationToken ct)
        {
            Contract.Requires(targetStream != null);
            Contract.Requires(replyStream != null);

            long chunks = 0L;
            long bytes = 0L;
            using (BufferedReadStream grpcStream = new BufferedReadStream(async () =>
            {
                if (await replyStream.MoveNext(ct))
                {
                    chunks++;
                    bytes += replyStream.Current.Content.Length;
                    return replyStream.Current.Content.ToByteArray();
                }
                else
                {
                    return null;
                }
            }))
            {
                using (Stream decompressedStream = new GZipStream(grpcStream, CompressionMode.Decompress, true))
                {
                    await decompressedStream.CopyToAsync(targetStream, _bufferSize, ct);
                }
            }

            return (chunks, bytes);
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
            if (ShutdownStarted && !ShutdownCompleted)
            {
                throw new CacheException($"{nameof(GrpcCopyClient)} must be shutdown before disposing.");
            }
        }
    }
}
