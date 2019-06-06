// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStore.Grpc;
using Grpc.Core;
// Can't rename ProtoBuf

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
        internal DateTime _lastUseTime;
        private int _uses;

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        internal GrpcCopyClientKey Key { get; private set; }

        public int Uses
        {
            get
            {
                return _uses;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcCopyClient" /> class.
        /// </summary>
        internal GrpcCopyClient(GrpcCopyClientKey key)
        {
            GrpcEnvironment.InitializeIfNeeded();
            _channel = new Channel(key.Host, key.GrpcPort, ChannelCredentials.Insecure);
            _client = new ContentServer.ContentServerClient(_channel);
            Key = key;

            _lastUseTime = DateTime.MinValue;
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
        public Task<CopyFileResult> CopyFileAsync(Context context, ContentHash hash, AbsolutePath destinationPath, CancellationToken ct = default(CancellationToken))
        {
            Func<Stream> streamFactory = () => new FileStream(destinationPath.Path, FileMode.Create, FileAccess.Write, FileShare.None, ContentStore.Grpc.CopyConstants.DefaultBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return CopyToAsync(context, hash, streamFactory, ct);        
        }
        
        /// <summary>
        /// Copies content from the server to the given stream.
        /// </summary>
        public Task<CopyFileResult> CopyToAsync(Context context, ContentHash hash, Stream stream, CancellationToken ct = default(CancellationToken))
        {
            return CopyToAsync(context, hash, () => stream, ct);
        }

        /// <summary>
        /// Copies content from the server to the stream returned by the factory.
        /// </summary>
        public async Task<CopyFileResult> CopyToAsync(Context context, ContentHash hash, Func<Stream> streamFactory, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                CopyFileRequest request = new CopyFileRequest()
                {
                    TraceId = context.Id.ToString(),
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
                            Enum.TryParse<CopyCompression>(header.Value, out compression);
                            break;
                    }
                }

                // Process reported server-side errors.
                if (exception != null)
                {
                    Debug.Assert(message != null);
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
                            await StreamContentAsync(targetStream, response.ResponseStream, ct).ConfigureAwait(false);
                            break;
                        case CopyCompression.Gzip:
                            await StreamContentWithCompressionAsync(targetStream, response.ResponseStream, ct).ConfigureAwait(false);
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
        /// Attempt to reserve the client. Fails if marked for shutdown.
        /// </summary>
        /// <param name="reused">Whether the client has been used previously.</param>
        /// <returns>Whether the client is approved for use.</returns>
        public bool TryAcquire(out bool reused)
        {
            lock (this)
            {
                _uses++;

                reused = _lastUseTime != DateTime.MinValue;
                if (_uses > 0)
                {
                    _lastUseTime = DateTime.UtcNow;
                    return true;
                }
            }

            reused = false;
            return false;
        }

        /// <summary>
        /// Attempt to prepare the client for shutdown, based on current uses and last use time.
        /// </summary>
        /// <param name="force">Whether last use time should be ignored.</param>
        /// <param name="earliestLastUseTime">If the client has been used since this time, then it is available for shutdown.</param>
        /// <returns>Whether the client can be marked for shutdown.</returns>
        public bool TryMarkForShutdown(bool force, DateTime earliestLastUseTime)
        {
            lock (this)
            {
                if (_uses == 0 && (force || _lastUseTime < earliestLastUseTime))
                {
                    _uses = int.MinValue;
                    return true;
                }
            }

            return false;
        }

        private async Task<(long Chunks, long Bytes)> StreamContentAsync(Stream targetStream, IAsyncStreamReader<CopyFileResponse> replyStream, CancellationToken ct = default(CancellationToken))
        {
            long chunks = 0L;
            long bytes = 0L;
            while (await replyStream.MoveNext(ct).ConfigureAwait(false))
            {
                chunks++;
                CopyFileResponse reply = replyStream.Current;
                bytes += reply.Content.Length;
                reply.Content.WriteTo(targetStream);
            }
            return (chunks, bytes);
        }

        private async Task<(long Chunks, long Bytes)> StreamContentWithCompressionAsync(Stream targetStream, IAsyncStreamReader<CopyFileResponse> replyStream, CancellationToken ct = default(CancellationToken))
        {
            Debug.Assert(targetStream != null);
            Debug.Assert(replyStream != null);

            long chunks = 0L;
            long bytes = 0L;
            using (BufferedReadStream grpcStream = new BufferedReadStream(async () =>
            {
                if (await replyStream.MoveNext(ct).ConfigureAwait(false))
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
                    await decompressedStream.CopyToAsync(targetStream, ContentStore.Grpc.CopyConstants.DefaultBufferSize, ct).ConfigureAwait(false);
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
            lock (this)
            {
                _uses--;
            }
        }
    }
}
