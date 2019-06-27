// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStore.Grpc;
using Google.Protobuf;
using Grpc.Core;
using PinRequest = ContentStore.Grpc.PinRequest;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// A CAS server implementation based on GRPC.
    /// </summary>
    public class GrpcContentServer
    {
        private readonly Capabilities _serviceCapabilities;
        private readonly ILogger _logger;
        private readonly Dictionary<string, IContentStore> _contentStoreByCacheName;
        private readonly ISessionHandler<IContentSession> _sessionHandler;
        private readonly ContentServerAdapter _adapter;
        private readonly int _bufferSize;
        private readonly int _gzipSizeBarrier;
        private readonly ByteArrayPool _pool;

        /// <nodoc />
        public GrpcContentServer(
            ILogger logger,
            Capabilities serviceCapabilities,
            ISessionHandler<IContentSession> sessionHandler,
            Dictionary<string, IContentStore> storesByName,
            LocalServerConfiguration localServerConfiguration = null)
        {
            _logger = logger;
            _serviceCapabilities = serviceCapabilities;
            _sessionHandler = sessionHandler;
            _adapter = new ContentServerAdapter(this);
            _contentStoreByCacheName = storesByName;
            _bufferSize = localServerConfiguration?.BufferSizeForGrpcCopies ?? ContentStore.Grpc.CopyConstants.DefaultBufferSize;
            _gzipSizeBarrier = localServerConfiguration?.GzipBarrierSizeForGrpcCopies ?? _bufferSize * 8;
            _pool = new ByteArrayPool(_bufferSize);
        }

        /// <nodoc />
        public ServerServiceDefinition Bind() => ContentServer.BindService(_adapter);

        /// <summary>
        /// Implements a create session request.
        /// </summary>
        public async Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request, CancellationToken token)
        {
            OperationStarted();

            var cacheContext = new Context(new Guid(request.TraceId), _logger);
            var sessionCreationResult = await _sessionHandler.CreateSessionAsync(
                new OperationContext(cacheContext, token),
                request.SessionName,
                request.CacheName,
                (ImplicitPin)request.ImplicitPin,
                (Capabilities)request.Capabilities);

            if (sessionCreationResult)
            {
                return new CreateSessionResponse()
                {
                    SessionId = sessionCreationResult.Value.sessionId,
                    TempDirectory = sessionCreationResult.Value.tempDirectory.Path
                };
            }
            else
            {
                return new CreateSessionResponse()
                {
                    ErrorMessage = sessionCreationResult.ErrorMessage
                };
            }
        }
        /// <summary>
        /// Implements a shutdown request for a session.
        /// </summary>
        public async Task<ShutdownResponse> ShutdownSessionAsync(ShutdownRequest request, CancellationToken token)
        {
            var cacheContext = new Context(new Guid(request.Header.TraceId), _logger);
            await _sessionHandler.ReleaseSessionAsync(new OperationContext(cacheContext, token), request.Header.SessionId);
            return new ShutdownResponse();
        }

        /// <nodoc />
        private Task<HelloResponse> HelloAsync(HelloRequest request, CancellationToken token)
        {
            OperationStarted();

            return Task.FromResult(
                new HelloResponse
                {
                    Success = true,
                    Capabilities = (int)_serviceCapabilities
                });
        }

        /// <nodoc />
        public async Task<GetStatsResponse> GetStatsAsync(GetStatsRequest request, CancellationToken token)
        {
            var cacheContext = new Context(Guid.NewGuid(), _logger);
            var counters = await _sessionHandler.GetStatsAsync(new OperationContext(cacheContext, token));
            if (!counters)
            {
                return GetStatsResponse.Failure();
            }

            return GetStatsResponse.Create(counters.Value.ToDictionaryIntegral());
        }

        /// <summary>
        /// Implements an update tracker request.
        /// TODO: Handle targeting of different stores. (bug 1365340)
        /// </summary>
        private async Task<RemoveFromTrackerResponse> RemoveFromTrackerAsync(
            RemoveFromTrackerRequest request,
            CancellationToken token)
        {
            OperationStarted();

            DateTime startTime = DateTime.UtcNow;
            var cacheContext = new Context(new Guid(request.TraceId), _logger);
            long filesEvicted = 0;
            var removeFromTrackerResult = await _sessionHandler.RemoveFromTrackerAsync(new OperationContext(cacheContext, token));
            if (!removeFromTrackerResult)
            {
                return new RemoveFromTrackerResponse
                {
                    Header = ResponseHeader.Failure(startTime, removeFromTrackerResult.ErrorMessage, removeFromTrackerResult.Diagnostics)
                };
            }

            filesEvicted = removeFromTrackerResult.Value;

            return new RemoveFromTrackerResponse
            {
                Header = ResponseHeader.Success(startTime),
                FilesEvicted = filesEvicted
            };
        }

        private async Task<OpenStreamResult> GetFileStreamAsync(Context context, ContentHash hash)
        {
            Debug.Assert(_contentStoreByCacheName != null);

            // Iterate through all known stores, looking for content in each.
            // In most of our configurations there is just one store anyway,
            // and doing this means both we can callers don't have
            // to deal with cache roots and drive letters.

            foreach (KeyValuePair<string, IContentStore> entry in _contentStoreByCacheName)
            {
                IStreamStore store = entry.Value as IStreamStore;
                if (store != null)
                {
                    OpenStreamResult result = await store.StreamContentAsync(context, hash);
                    if (result.Code != OpenStreamResult.ResultCode.ContentNotFound)
                    {
                        return result;
                    }
                }
            }

            return new OpenStreamResult(OpenStreamResult.ResultCode.ContentNotFound, $"{hash} to found");
        }

        private async Task<ExistenceResponse> CheckFileExistsAsync(ExistenceRequest request, CancellationToken token)
        {
            OperationStarted();

            Debug.Assert(_contentStoreByCacheName != null);

            DateTime startTime = DateTime.UtcNow;
            Context cacheContext = new Context(new Guid(request.TraceId), _logger);
            HashType type = (HashType)request.HashType;
            ContentHash hash = request.ContentHash.ToContentHash((HashType)request.HashType);

            // Iterate through all known stores, looking for content in each.
            // In most of our configurations there is just one store anyway,
            // and doing this means both we can callers don't have
            // to deal with cache roots and drive letters.

            foreach (KeyValuePair<string, IContentStore> entry in _contentStoreByCacheName)
            {
                IStreamStore store = entry.Value as IStreamStore;
                if (store != null)
                {
                    FileExistenceResult result = await store.CheckFileExistsAsync(cacheContext, hash);
                    if (result.Succeeded)
                    {
                        return new ExistenceResponse{ Header = ResponseHeader.Success(startTime) };
                    }
                }
            }

            return new ExistenceResponse { Header = ResponseHeader.Failure(startTime, $"{hash} not found in the cache") };
        }

        /// <summary>
        /// Implements a copy file request.
        /// </summary>
        private async Task CopyFileAsync(CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext context)
        {
            try
            {
                OperationStarted();

                // Get the content stream.
                Context cacheContext = new Context(new Guid(request.TraceId), _logger);
                HashType type = (HashType)request.HashType;
                ContentHash hash = request.ContentHash.ToContentHash((HashType)request.HashType);
                OpenStreamResult result = await GetFileStreamAsync(cacheContext, hash);

                using (result.Stream)
                {
                    // Figure out response headers.
                    CopyCompression compression = CopyCompression.None;
                    Metadata headers = new Metadata();
                    switch (result.Code)
                    {
                        case OpenStreamResult.ResultCode.ContentNotFound:
                            headers.Add("Exception", "ContentNotFound");
                            headers.Add("Message", $"Requested content at {hash} not found.");
                            break;
                        case OpenStreamResult.ResultCode.Error:
                            Debug.Assert(result.Exception != null);
                            headers.Add("Exception", result.Exception.GetType().Name);
                            headers.Add("Message", result.Exception.Message);
                            break;
                        case OpenStreamResult.ResultCode.Success:
                            Debug.Assert(result.Stream != null);
                            long size = result.Stream.Length;
                            headers.Add("FileSize", size.ToString());
                            if ((request.Compression == CopyCompression.Gzip) && (size > _gzipSizeBarrier))
                            {
                                compression = CopyCompression.Gzip;
                            }
                            headers.Add("Compression", compression.ToString());
                            headers.Add("ChunkSize", _bufferSize.ToString());
                            break;
                        default:
                            throw new NotImplementedException();
                    }

                    // Send the response headers.
                    await context.WriteResponseHeadersAsync(headers);

                    // Send the content.
                    if (result.Succeeded)
                    {
                        _logger.Debug($"Streaming file through GRPC with GZip {(compression == CopyCompression.Gzip ? "on" : "off")}");

                        using (var arrayHandle = _pool.Get())
                        {
                            byte[] buffer = arrayHandle.Value;
                            switch (compression)
                            {
                                case CopyCompression.None:
                                    await StreamContentAsync(result.Stream, buffer, responseStream, context.CancellationToken);
                                    break;
                                case CopyCompression.Gzip:
                                    await StreamContentWithCompressionAsync(result.Stream, buffer, responseStream, context.CancellationToken);
                                    break;
                            }
                        }
                    }
                }
            
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async Task<(long Chunks, long Bytes)> StreamContentAsync(Stream reader, byte[] buffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct = default(CancellationToken))
        {
            Debug.Assert(!(reader is null));
            Debug.Assert(!(responseStream is null));

            int chunkSize = 0;
            long chunks = 0L;
            long bytes = 0L;

            // Pre-fill buffer with the file's first chunk
            await ReadNextChunk();

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (chunkSize == 0) { break; }

                ByteString content = ByteString.CopyFrom(buffer, 0, chunkSize);
                CopyFileResponse response = new CopyFileResponse() { Content = content, Index = chunks };

                bytes += chunkSize;
                chunks++;

                // Read the next chunk while waiting for the response
                await Task.WhenAll(ReadNextChunk(), responseStream.WriteAsync(response));
            }

            return (chunks, bytes);

            async Task<int> ReadNextChunk() { chunkSize = await reader.ReadAsync(buffer, 0, buffer.Length, ct); return chunkSize; }
        }

        private async Task<(long Chunks, long Bytes)> StreamContentWithCompressionAsync(Stream reader, byte[] buffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct = default(CancellationToken))
        {
            Debug.Assert(!(reader is null));
            Debug.Assert(!(responseStream is null));

            long bytes = 0L;
            long chunks = 0L;
            using (Stream grpcStream = new BufferedWriteStream(
                buffer,
                async (byte[] bf, int offset, int count) =>
                {
                    ByteString content = ByteString.CopyFrom(bf, offset, count);
                    CopyFileResponse response = new CopyFileResponse() { Content = content, Index = chunks };
                    await responseStream.WriteAsync(response);
                    bytes += count;
                    chunks++;
                }
            ))
            {
                using (Stream compressionStream = new GZipStream(grpcStream, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    await reader.CopyToAsync(compressionStream, buffer.Length, ct).ConfigureAwait(false);
                    await compressionStream.FlushAsync().ConfigureAwait(false);
                }
                await grpcStream.FlushAsync().ConfigureAwait(false);
            }

            return (chunks, bytes);

        }


        /// <summary>
        /// Implements a pin request.
        /// </summary>
        private async Task<PinResponse> PinAsync(PinRequest request, CancellationToken token)
        {
            OperationStarted();

            DateTime startTime = DateTime.UtcNow;
            var cacheContext = new Context(new Guid(request.Header.TraceId), _logger);
            return await RunFuncAndReportAsync(
                request.Header.SessionId,
                async session =>
                {
                    PinResult pinResult = await session.PinAsync(
                        cacheContext,
                        request.ContentHash.ToContentHash((HashType)request.HashType),
                        token);
                    return new PinResponse
                    {
                        Header = new ResponseHeader(
                                   startTime, pinResult.Succeeded, (int)pinResult.Code, pinResult.ErrorMessage, pinResult.Diagnostics)
                    };
                },
                errorMessage =>
                    new PinResponse { Header = ResponseHeader.Failure(startTime, (int)PinResult.ResultCode.Error, errorMessage) });
        }

        /// <summary>
        /// Bulk pin content hashes.
        /// </summary>
        private async Task<PinBulkResponse> PinBulkAsync(PinBulkRequest request, CancellationToken token)
        {
            OperationStarted();

            DateTime startTime = DateTime.UtcNow;
            var cacheContext = new Context(new Guid(request.Header.TraceId), _logger);
            return await RunFuncAndReportAsync(
                request.Header.SessionId,
                async session =>
                {
                    var pinList = new List<ContentHash>();
                    foreach (var hash in request.Hashes)
                    {
                        pinList.Add(hash.ContentHash.ToContentHash((HashType)hash.HashType));
                    }

                    List<Task<Indexed<PinResult>>> pinResults = (await session.PinAsync(
                        cacheContext,
                        pinList,
                        token)).ToList();
                    var response = new PinBulkResponse();
                    try
                    {
                        foreach (var pinResult in pinResults)
                        {
                            var result = await pinResult;
                            var responseHeader = new ResponseHeader(
                                startTime,
                                result.Item.Succeeded,
                                (int)result.Item.Code,
                                result.Item.ErrorMessage,
                                result.Item.Diagnostics);
                            response.Header.Add(result.Index, responseHeader);
                        }
                    }
                    catch (Exception)
                    {
                        pinResults.ForEach(task => task.FireAndForget(cacheContext));
                        throw;
                    }

                    return response;
                },
                errorMessage =>
                {
                    var header = ResponseHeader.Failure(startTime, (int)PinResult.ResultCode.Error, errorMessage);
                    var response = new PinBulkResponse();
                    int i = 0;
                    foreach (var hash in request.Hashes)
                    {
                        response.Header.Add(i, header);
                        i++;
                    }
                    return response;
                });
        }

        /// <summary>
        /// Implements a place file request.
        /// </summary>
        private async Task<PlaceFileResponse> PlaceFileAsync(PlaceFileRequest request, CancellationToken token)
        {
            OperationStarted();

            DateTime startTime = DateTime.UtcNow;
            var cacheContext = new Context(new Guid(request.Header.TraceId), _logger);
            return await RunFuncAndReportAsync(
                request.Header.SessionId,
                async session =>
                {
                    PlaceFileResult placeFileResult = await session.PlaceFileAsync(
                        cacheContext,
                        request.ContentHash.ToContentHash((HashType)request.HashType),
                        new AbsolutePath(request.Path),
                        (FileAccessMode)request.FileAccessMode,
                        FileReplacementMode.ReplaceExisting, // Hard-coded because the service can't tell if this is a retry (where the previous try may have left a partial file)
                        (FileRealizationMode)request.FileRealizationMode,
                        token);
                    return new PlaceFileResponse
                    {
                        Header =
                                   new ResponseHeader(
                                       startTime,
                                       placeFileResult.Succeeded,
                                       (int)placeFileResult.Code,
                                       placeFileResult.ErrorMessage,
                                       placeFileResult.Diagnostics),
                        ContentSize = placeFileResult.FileSize
                    };
                },
                errorMessage => new PlaceFileResponse
                {
                    Header = ResponseHeader.Failure(startTime, (int)PlaceFileResult.ResultCode.Error, errorMessage)
                });
        }

        /// <summary>
        /// Implements a put file request.
        /// </summary>
        private async Task<PutFileResponse> PutFileAsync(PutFileRequest request, CancellationToken token)
        {
            OperationStarted();

            DateTime startTime = DateTime.UtcNow;
            var cacheContext = new Context(new Guid(request.Header.TraceId), _logger);
            return await RunFuncAndReportAsync(
                request.Header.SessionId,
                async session =>
                {
                    PutResult putResult;
                    if (request.ContentHash == ByteString.Empty)
                    {
                        putResult = await session.PutFileAsync(
                            cacheContext,
                            (HashType)request.HashType,
                            new AbsolutePath(request.Path),
                            (FileRealizationMode)request.FileRealizationMode,
                            token);
                    }
                    else
                    {
                        putResult = await session.PutFileAsync(
                            cacheContext,
                            request.ContentHash.ToContentHash((HashType)request.HashType),
                            new AbsolutePath(request.Path),
                            (FileRealizationMode)request.FileRealizationMode,
                            token);
                    }

                    return new PutFileResponse
                    {
                        Header =
                                   new ResponseHeader(
                                       startTime,
                                       putResult.Succeeded,
                                       putResult.Succeeded ? 0 : 1,
                                       putResult.ErrorMessage,
                                       putResult.Diagnostics),
                        ContentSize = putResult.ContentSize,
                        ContentHash = putResult.ContentHash.ToByteString(),
                        HashType = (int)putResult.ContentHash.HashType
                    };
                },
                errorMessage => new PutFileResponse { Header = ResponseHeader.Failure(startTime, errorMessage) });
        }

        /// <summary>
        /// Implements a heartbeat request for a session.
        /// </summary>
        private Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken token)
        {
            OperationStarted();

            DateTime startTime = DateTime.UtcNow;
            return RunFuncAndReportAsync(
                request.Header.SessionId,
                session => Task.FromResult(new HeartbeatResponse { Header = ResponseHeader.Success(startTime) }),
                errorMessage => new HeartbeatResponse { Header = ResponseHeader.Failure(startTime, errorMessage) });
        }

        private void OperationStarted([CallerMemberName]string requestType = "Unknown")
        {
            // TODO: Add counter, but don't trace
        }

        private async Task<T> RunFuncAndReportAsync<T>(
            int sessionId,
            Func<IContentSession, Task<T>> taskFunc,
            Func<string, T> failFunc)
        {
            if (!_sessionHandler.TryGetSession(sessionId, out var session))
            {
                return failFunc($"Could not find session for session ID {sessionId}");
            }

            // TODO ST: the code is polluted with Task.Yield with no comments.
            await Task.Yield();

            try
            {
                return await taskFunc(session);
            }
            catch (TaskCanceledException)
            {
                _logger.Info("GRPC server operation canceled.");
                return failFunc("The operation was canceled.");
            }
            catch (Exception e)
            {
                _logger.Error(e, "GRPC server operation failed.");
                return failFunc(e.ToString());
            }
        }

        private class ContentServerAdapter : global::ContentStore.Grpc.ContentServer.ContentServerBase
        {
            private readonly GrpcContentServer _contentServer;

            /// <inheritdoc />
            public ContentServerAdapter(GrpcContentServer contentServer)
            {
                _contentServer = contentServer;
            }

            /// <inheritdoc />
            public override Task<ExistenceResponse> CheckFileExists(ExistenceRequest request, ServerCallContext context) => _contentServer.CheckFileExistsAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task CopyFile(CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext context) => _contentServer.CopyFileAsync(request, responseStream, context);

            /// <inheritdoc />
            public override Task<HelloResponse> Hello(HelloRequest request, ServerCallContext context) => _contentServer.HelloAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<GetStatsResponse> GetStats(GetStatsRequest request, ServerCallContext context) => _contentServer.GetStatsAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<CreateSessionResponse> CreateSession(CreateSessionRequest request, ServerCallContext context) => _contentServer.CreateSessionAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<PinResponse> Pin(PinRequest request, ServerCallContext context) => _contentServer.PinAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<PlaceFileResponse> PlaceFile(PlaceFileRequest request, ServerCallContext context) => _contentServer.PlaceFileAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<PinBulkResponse> PinBulk(PinBulkRequest request, ServerCallContext context) => _contentServer.PinBulkAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<PutFileResponse> PutFile(PutFileRequest request, ServerCallContext context) => _contentServer.PutFileAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<ShutdownResponse> ShutdownSession(ShutdownRequest request, ServerCallContext context) => _contentServer.ShutdownSessionAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request, ServerCallContext context) => _contentServer.HeartbeatAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<RemoveFromTrackerResponse> RemoveFromTracker(RemoveFromTrackerRequest request, ServerCallContext context) => _contentServer.RemoveFromTrackerAsync(request, context.CancellationToken);
        }
    }
}
