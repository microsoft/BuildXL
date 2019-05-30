// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
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
        // Link to DistributedContentStoreSettings.MaxConcurrentCopyOperations
        private const int _maxClientCount = 512;

        private static readonly object lockObject = new object();
        internal static readonly ConcurrentDictionary<(string, int, bool), GrpcCopyClient> _clientDict = new ConcurrentDictionary<(string, int, bool), GrpcCopyClient>(Environment.ProcessorCount, _maxClientCount);
        private static Task _backgroundCleaningTask;
        private static CancellationTokenSource _backgroundCleaningTaskTokenSource;

        private readonly Channel _channel;
        private readonly ContentServer.ContentServerClient _client;
        private readonly string _host;
        private readonly int _grpcPort;
        private bool _useCompression;
        internal DateTime _lastUseTime;
        internal int _uses;

        static GrpcCopyClient()
        {
            RestartBackgroundCleanup();
        }

        private async static Task BackgroundCleanupAsync(CancellationTokenSource cts)
        {
            _backgroundCleaningTaskTokenSource = cts;
            var ct = _backgroundCleaningTaskTokenSource.Token;

            while (!ct.IsCancellationRequested)
            {
                if (!_clientDict.IsEmpty)
                {
                    var oneHourAgo = DateTime.UtcNow - TimeSpan.FromHours(1);

                    foreach (var kvp in _clientDict)
                    {
                        var client = kvp.Value;
                        if (client._uses == 0 && client._lastUseTime < oneHourAgo)
                        {
                            lock (lockObject)
                            {
                                if (_clientDict.TryRemove((client._host, client._grpcPort, client._useCompression), out GrpcCopyClient removedClient))
                                {
                                    // Cannot await within a lock
                                    client._channel.ShutdownAsync().Start();
                                }
                            }
                        }
                    }
                }

                await Task.Delay(1000 * 60 * 30, ct);
            }
        }

        internal static void RestartBackgroundCleanup()
        {
            lock (lockObject)
            {
                if (_backgroundCleaningTask != null)
                {
                    _backgroundCleaningTaskTokenSource.Cancel();
                    try
                    {
                        _backgroundCleaningTask.Wait();
                    }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                    catch (AggregateException) { }
#pragma warning restore ERP022  // Unobserved exception in generic exception handler
                }

                _backgroundCleaningTask = Task.Run(() => BackgroundCleanupAsync(new CancellationTokenSource()));
            }
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcCopyClient" /> class.
        /// </summary>
        private GrpcCopyClient(string host, int grpcPort, bool useCompression)
        {
            GrpcEnvironment.InitializeIfNeeded();
            _channel = new Channel(host, grpcPort, ChannelCredentials.Insecure);
            _client = new ContentServer.ContentServerClient(_channel);
            _host = host;
            _grpcPort = grpcPort;
            _useCompression = useCompression;

            _lastUseTime = DateTime.UtcNow;
            _uses = 0;
        }

        /// <summary>
        /// Use an existing GRPC client if possible, else create a new one.
        /// </summary>
        /// <param name="host">Name of the host for the server (e.g. 'localhost').</param>
        /// <param name="grpcPort">GRPC port on the server.</param>
        /// <param name="useCompression">Whether or not GZip is enabled for copies.</param>
        public static GrpcCopyClient Create(string host, int grpcPort, bool useCompression = false)
        {
            if (_clientDict.TryGetValue((host, grpcPort, useCompression), out GrpcCopyClient existingClient))
            {
                Interlocked.Increment(ref existingClient._uses);
                existingClient._lastUseTime = DateTime.UtcNow;
                return existingClient;
            }
            else if (_clientDict.Count > _maxClientCount)
            {
                throw new CacheException($"Attempting to create {nameof(GrpcCopyClient)} to increase cached count above maximum allowed ({_maxClientCount})");
            }
            else
            {
                // TODO: Replace `tup` with named tuple when allowed by C# compiler
                var foundClient = _clientDict.GetOrAdd((host, grpcPort, useCompression), tup => {
                    var newClient = new GrpcCopyClient(tup.Item1, tup.Item2, tup.Item3);
                    newClient._lastUseTime = DateTime.UtcNow;
                    return newClient;
                    });
                Interlocked.Increment(ref foundClient._uses);
                return foundClient;
            }
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
                    Compression = _useCompression ? CopyCompression.Gzip : CopyCompression.None
                };

                AsyncServerStreamingCall<CopyFileResponse> response = _client.CopyFile(request);

                Metadata headers = await response.ResponseHeadersAsync;

                // If the remote machine couldn't be contacted, GRPC returns an empty
                // header collection. GRPC would throw an RpcException when we tried
                // to stream response, but by that time we would have created target
                // stream. To avoid that, exit early instead.
                if (headers.Count == 0)
                {
                    return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, $"Failed to connect to copy server {_host} at port {_grpcPort}.");
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
            Interlocked.Decrement(ref _uses);
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
