// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using BuildXL.Utilities.Tasks;
using ContentStore.Grpc;
using Google.Protobuf;
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

        /// <inheritdoc />
        protected override Func<BoolResult, string> ExtraStartupMessageFactory => _ => Key.ToString();

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcCopyClient" /> class.
        /// </summary>
        internal GrpcCopyClient(GrpcCopyClientKey key, int? clientBufferSize)
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
            // We had seen case, when the following call was blocked effectively forever.
            // Adding external timeout to force a failure instead of waiting forever.
            await _channel.ShutdownAsync().WithTimeoutAsync(TimeSpan.FromSeconds(30));
            return BoolResult.Success;
        }

        /// <summary>
        /// Checks if file exists on remote machine.
        /// </summary>
        public async Task<FileExistenceResult> CheckFileExistsAsync(Context context, ContentHash hash)
        {
            try
            {
                var request = new ExistenceRequest()
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
        public async Task<CopyFileResult> CopyFileAsync(Context context, ContentHash hash, AbsolutePath destinationPath, CancellationToken ct)
        {
            Func<Stream> streamFactory = () => new FileStream(destinationPath.Path, FileMode.Create, FileAccess.Write, FileShare.None, _bufferSize, FileOptions.SequentialScan);

            using (var operationContext = TrackShutdown(context, ct))
            {
                return await CopyToCoreAsync(operationContext, hash, streamFactory);
            }
        }

        /// <summary>
        /// Copies content from the server to the given stream.
        /// </summary>
        public async Task<CopyFileResult> CopyToAsync(Context context, ContentHash hash, Stream stream, CancellationToken ct)
        {
            using (var operationContext = TrackShutdown(context, ct))
            {
                // If a stream is passed from the outside this operation should not be closing it.
                return await CopyToCoreAsync(operationContext, hash, () => stream, closeStream: false);
            }
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
        private async Task<CopyFileResult> CopyToCoreAsync(OperationContext context, ContentHash hash, Func<Stream> streamFactory, bool closeStream = true)
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

                AsyncServerStreamingCall<CopyFileResponse> response = _client.CopyFile(request, cancellationToken: context.Token);

                Metadata headers = await response.ResponseHeadersAsync;

                // If the remote machine couldn't be contacted, GRPC returns an empty
                // header collection. GRPC would throw an RpcException when we tried
                // to stream response, but by that time we would have created target
                // stream. To avoid that, exit early instead.
                if (headers.Count == 0)
                {
                    return new CopyFileResult(CopyResultCode.ServerUnavailable, $"Failed to connect to copy server {Key.Host} at port {Key.GrpcPort}.");
                }

                // Parse header collection.
                string? exception = null;
                string? message = null;
                CopyCompression compression = CopyCompression.None;
                foreach (Metadata.Entry header in headers)
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
                            return new CopyFileResult(CopyResultCode.FileNotFoundError, message);
                        default:
                            return new CopyFileResult(CopyResultCode.UnknownServerError, message);
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
                    return new CopyFileResult(CopyResultCode.DestinationPathError, targetException);
                }

                // Copy the content to the target stream.
                try
                {
                    switch (compression)
                    {
                        case CopyCompression.None:
                            await StreamContentAsync(targetStream, response.ResponseStream, context.Token);
                            break;
                        case CopyCompression.Gzip:
                            await StreamContentWithCompressionAsync(targetStream, response.ResponseStream, context.Token);
                            break;
                        default:
                            throw new NotSupportedException($"CopyCompression {compression} is not supported.");
                    }
                }
                finally
                {
                    if (closeStream)
                    {
#pragma warning disable AsyncFixer02 // A disposable object used in a fire & forget async call
                        targetStream.Dispose();
#pragma warning restore AsyncFixer02 // A disposable object used in a fire & forget async call
                    }
                }

                return CopyFileResult.Success;
            }
            catch (RpcException r)
            {
                if (r.StatusCode == StatusCode.Unavailable)
                {
                    return new CopyFileResult(CopyResultCode.ServerUnavailable, r);
                }
                else
                {
                    return new CopyFileResult(CopyResultCode.Unknown, r);
                }
            }
        }

        /// <summary>
        /// Requests host to copy a file from another source machine.
        /// </summary>
        public async Task<BoolResult> RequestCopyFileAsync(OperationContext context, ContentHash hash)
        {
            try
            {
                var request = new RequestCopyFileRequest
                {
                    TraceId = context.TracingContext.Id.ToString(),
                    ContentHash = hash.ToByteString(),
                    HashType = (int)hash.HashType
                };

                var response = await _client.RequestCopyFileAsync(request, cancellationToken: context.Token);

                return response.Header.Succeeded
                    ? BoolResult.Success
                    : new BoolResult(response.Header.ErrorMessage);
            }
            catch (RpcException r)
            {
                return new BoolResult(r);
            }
        }

        /// <summary>
        /// Pushes content to another machine.
        /// </summary>
        public async Task<PushFileResult> PushFileAsync(OperationContext context, ContentHash hash, Stream stream)
        {
            try
            {
                var pushRequest = new PushRequest(hash, context.TracingContext.Id);
                var headers = pushRequest.GetMetadata();

                using var call = _client.PushFile(headers, cancellationToken: context.Token);
                var requestStream = call.RequestStream;

                var responseHeaders = await call.ResponseHeadersAsync;

                // If the remote machine couldn't be contacted, GRPC returns an empty
                // header collection. To avoid an exception, exit early instead.
                if (responseHeaders.Count == 0)
                {
                    return PushFileResult.ServerUnavailable();
                }

                var pushResponse = PushResponse.FromMetadata(responseHeaders);
                if (!pushResponse.ShouldCopy)
                {
                    return PushFileResult.Rejected(pushResponse.Rejection);
                }

                // If we get a response before we finish streaming, it must be that the server cancelled the operation.
                using var serverIsDoneSource = new CancellationTokenSource();
                var pushCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(serverIsDoneSource.Token, context.Token).Token;

                var responseStream = call.ResponseStream;
                var responseMoveNext = responseStream.MoveNext(context.Token);

                var responseCompletedTask = responseMoveNext.ContinueWith(
                    t =>
                    {
                        // It is possible that the next operation in this method will fail
                        // causing stack unwinding that will dispose serverIsDoneSource.
                        //
                        // Then when responseMoveNext is done serverIsDoneSource is already disposed and
                        // serverIsDoneSource.Cancel will throw ObjectDisposedException.
                        // This exception is not observed because the stack could've been unwound before
                        // the result of this method is awaited.
                        IgnoreObjectDisposedException(() => serverIsDoneSource.Cancel());
                    });

                await StreamContentAsync(stream, new byte[_bufferSize], requestStream, pushCancellationToken);

                context.Token.ThrowIfCancellationRequested();

                await requestStream.CompleteAsync();

                await responseCompletedTask;

                // Make sure that we only attempt to read response when it is available.
                var responseIsAvailable = await responseMoveNext;
                if (!responseIsAvailable)
                {
                    return new PushFileResult("Failed to get final response.");
                }

                var response = responseStream.Current;

                return response.Header.Succeeded
                    ? PushFileResult.PushSucceeded()
                    : new PushFileResult(response.Header.ErrorMessage);
            }
            catch (RpcException r)
            {
                return new PushFileResult(r);
            }
        }

        private static void IgnoreObjectDisposedException(Action action)
        {
            try
            {
                action();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task StreamContentAsync(Stream input, byte[] buffer, IClientStreamWriter<PushFileRequest> requestStream, CancellationToken ct)
        {
            Contract.Requires(!(input is null));
            Contract.Requires(!(requestStream is null));

            int chunkSize = 0;

            // Pre-fill buffer with the file's first chunk
            await readNextChunk();

            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (chunkSize == 0) { break; }

                ByteString content = ByteString.CopyFrom(buffer, 0, chunkSize);
                var request = new PushFileRequest() { Content = content };

                // Read the next chunk while waiting for the response
                await Task.WhenAll(readNextChunk(), requestStream.WriteAsync(request));
            }

            async Task<int> readNextChunk() { chunkSize = await input.ReadAsync(buffer, 0, buffer.Length, ct); return chunkSize; }
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
            using (var grpcStream = new BufferedReadStream(async () =>
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
