// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using ContentStore.Grpc;
using Google.Protobuf;
using Grpc.Core;
using PinRequest = ContentStore.Grpc.PinRequest;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// A CAS server implementation based on GRPC.
    /// </summary>
    public class GrpcContentServer : StartupShutdownSlimBase
    {
        private readonly Tracer _tracer = new Tracer(nameof(GrpcContentServer));

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        private readonly Capabilities _serviceCapabilities;
        private readonly IReadOnlyDictionary<string, IContentStore> _contentStoreByCacheName;
        private readonly int _bufferSize;
        private readonly int _gzipSizeBarrier;
        private readonly ByteArrayPool _pool;

        private readonly IAbsFileSystem _fileSystem;
        private readonly AbsolutePath _workingDirectory;
        private DisposableDirectory? _temporaryDirectory;

        /// <summary>
        /// This adapter routes messages from Grpc to the current class.
        /// </summary>
        /// <remarks>
        /// Expected to be read-only after construction. Child classes may overwrite the field in their constructor,
        /// but not afterwards, or behavior will be undefined.
        /// </remarks>
        protected ContentServerAdapter GrpcAdapter { get; set; }

        /// <nodoc />
        protected readonly ILogger Logger;

        /// <summary>
        /// Session handler for <see cref="IContentSession"/>
        /// </summary>
        /// <remarks>
        /// This is a hack to allow for an <see cref="ISessionHandler"/> with other sessions that inherit from
        /// <see cref="IContentSession"/> to be used instead.
        /// </remarks>
        protected virtual ISessionHandler<IContentSession> ContentSessionHandler { get; }

        /// <summary>
        /// A set of hashes currently handled by the server.
        /// </summary>
        private readonly ConcurrentBigSet<ContentHash> _ongoingPushes = new ConcurrentBigSet<ContentHash>();

        /// <summary>
        /// The max number of push handlers running at the same time.
        /// </summary>
        private readonly int _ongoingPushCountLimit;

        /// <nodoc />
        public GrpcContentServer(
            ILogger logger,
            Capabilities serviceCapabilities,
            ISessionHandler<IContentSession> sessionHandler,
            IReadOnlyDictionary<string, IContentStore> storesByName,
            LocalServerConfiguration? localServerConfiguration = null)
        {
            Contract.RequiresNotNull(storesByName);

            _serviceCapabilities = serviceCapabilities;
            _contentStoreByCacheName = storesByName;
            _bufferSize = localServerConfiguration?.BufferSizeForGrpcCopies ?? ContentStore.Grpc.CopyConstants.DefaultBufferSize;
            _gzipSizeBarrier = localServerConfiguration?.GzipBarrierSizeForGrpcCopies ?? (_bufferSize * 8);
            _ongoingPushCountLimit = localServerConfiguration?.ProactivePushCountLimit ?? LocalServerConfiguration.DefaultProactivePushCountLimit;

            _pool = new ByteArrayPool(_bufferSize);
            ContentSessionHandler = sessionHandler;

            _fileSystem = localServerConfiguration?.FileSystem ?? new PassThroughFileSystem();
            _workingDirectory = (localServerConfiguration?.DataRootPath ?? _fileSystem.GetTempPath()) / "GrpcContentServer";

            GrpcAdapter = new ContentServerAdapter(this);

            Logger = logger;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            _temporaryDirectory = new DisposableDirectory(_fileSystem, _workingDirectory / "temp");
            return BoolResult.SuccessTask;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _temporaryDirectory?.Dispose();
            return BoolResult.SuccessTask;
        }

        /// <nodoc />
        public ServerServiceDefinition[] Bind() => new ServerServiceDefinition[] { ContentServer.BindService(GrpcAdapter) };

        /// <summary>
        /// Implements a create session request.
        /// </summary>
        public async Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request, CancellationToken token)
        {
            var cacheContext = new Context(new Guid(request.TraceId), Logger);
            var sessionCreationResult = await ContentSessionHandler.CreateSessionAsync(
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
            var cacheContext = new Context(new Guid(request.Header.TraceId), Logger);
            await ContentSessionHandler.ReleaseSessionAsync(new OperationContext(cacheContext, token), request.Header.SessionId);
            return new ShutdownResponse();
        }

        /// <nodoc />
        private Task<HelloResponse> HelloAsync(HelloRequest request, CancellationToken token)
        {
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
            var cacheContext = new Context(Guid.NewGuid(), Logger);
            var counters = await ContentSessionHandler.GetStatsAsync(new OperationContext(cacheContext, token));
            if (!counters)
            {
                return GetStatsResponse.Failure();
            }

            return GetStatsResponse.Create(counters.Value!.ToDictionaryIntegral());
        }

        /// <summary>
        /// Implements an update tracker request.
        /// TODO: Handle targeting of different stores. (bug 1365340)
        /// </summary>
        private async Task<RemoveFromTrackerResponse> RemoveFromTrackerAsync(
            RemoveFromTrackerRequest request,
            CancellationToken token)
        {
            DateTime startTime = DateTime.UtcNow;
            var cacheContext = new Context(new Guid(request.TraceId), Logger);
            var removeFromTrackerResult = await ContentSessionHandler.RemoveFromTrackerAsync(new OperationContext(cacheContext, token));
            if (!removeFromTrackerResult)
            {
                return new RemoveFromTrackerResponse
                {
                    Header = ResponseHeader.Failure(startTime, removeFromTrackerResult.ErrorMessage, removeFromTrackerResult.Diagnostics)
                };
            }

            long filesEvicted = removeFromTrackerResult.Value;

            return new RemoveFromTrackerResponse
            {
                Header = ResponseHeader.Success(startTime),
                FilesEvicted = filesEvicted
            };
        }

        private async Task<OpenStreamResult> GetFileStreamAsync(Context context, ContentHash hash)
        {
            // Iterate through all known stores, looking for content in each.
            // In most of our configurations there is just one store anyway,
            // and doing this means both we can callers don't have
            // to deal with cache roots and drive letters.

            foreach (KeyValuePair<string, IContentStore> entry in _contentStoreByCacheName)
            {
                if (entry.Value is IStreamStore store)
                {
                    OpenStreamResult result = await store.StreamContentAsync(context, hash);
                    if (result.Code != OpenStreamResult.ResultCode.ContentNotFound)
                    {
                        return result;
                    }
                }
            }

            return new OpenStreamResult(OpenStreamResult.ResultCode.ContentNotFound, $"{hash.ToShortString()} to found");
        }

        private async Task<ExistenceResponse> CheckFileExistsAsync(ExistenceRequest request, CancellationToken token)
        {
            DateTime startTime = DateTime.UtcNow;
            Context cacheContext = new Context(new Guid(request.TraceId), Logger);
            ContentHash hash = request.ContentHash.ToContentHash((HashType)request.HashType);

            // Iterate through all known stores, looking for content in each.
            // In most of our configurations there is just one store anyway,
            // and doing this means both we can callers don't have
            // to deal with cache roots and drive letters.

            foreach (KeyValuePair<string, IContentStore> entry in _contentStoreByCacheName)
            {
                if (entry.Value is IStreamStore store)
                {
                    FileExistenceResult result = await store.CheckFileExistsAsync(cacheContext, hash);
                    if (result.Succeeded)
                    {
                        return new ExistenceResponse { Header = ResponseHeader.Success(startTime) };
                    }
                }
            }

            return new ExistenceResponse { Header = ResponseHeader.Failure(startTime, $"{hash.ToShortString()} not found in the cache") };
        }

        /// <summary>
        /// Implements a copy file request.
        /// </summary>
        private async Task CopyFileAsync(CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext context)
        {
            // Get the content stream.
            Context cacheContext = new Context(new Guid(request.TraceId), Logger);
            ContentHash hash = request.GetContentHash();
            OpenStreamResult result = await GetFileStreamAsync(cacheContext, hash);

            // If result is unsuccessful, then result.Stream is null, but using(null) is just a no op.
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
                        Contract.AssertNotNull(result.Exception);
                        headers.Add("Exception", result.Exception.GetType().Name);
                        headers.Add("Message", result.Exception.Message);
                        break;
                    case OpenStreamResult.ResultCode.Success:
                        Contract.AssertNotNull(result.Stream);
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
                        throw new NotImplementedException($"Unknown result.Code '{result.Code}'.");
                }
                
                // Send the response headers.
                await context.WriteResponseHeadersAsync(headers);

                // Send the content.
                if (result.Succeeded)
                {
                    var operationContext = new OperationContext(cacheContext, context.CancellationToken);

                    using (var arrayHandle = _pool.Get())
                    {
                        StreamContentDelegate streamContent = compression == CopyCompression.None ? (StreamContentDelegate)StreamContentAsync : StreamContentWithCompressionAsync;

                        byte[] buffer = arrayHandle.Value;
                        await operationContext.PerformOperationAsync(
                                _tracer,
                                () => streamContent(result.Stream!, buffer, responseStream, context.CancellationToken),
                                traceOperationStarted: false, // Tracing only stop messages
                                extraEndMessage: r => $"Hash={hash.ToShortString()}, GZip={(compression == CopyCompression.Gzip ? "on" : "off")}.")
                            .IgnoreFailure(); // The error was already logged.
                    }
                }
            }
        }

        /// <summary>
        /// Implements a request copy file request
        /// </summary>
        private Task<RequestCopyFileResponse> RequestCopyFileAsync(RequestCopyFileRequest request, CancellationToken cancellationToken)
        {
            ContentHash hash = request.ContentHash.ToContentHash((HashType)request.HashType);

            return RunFuncNoSessionAsync(
                request.TraceId, 
                async (context) =>
                {
                    // Iterate through all known stores, looking for content in each.
                    // In most of our configurations there is just one store anyway,
                    // and doing this means both we can callers don't have
                    // to deal with cache roots and drive letters.

                    if (_contentStoreByCacheName.Values.OfType<ICopyRequestHandler>().FirstOrDefault() is ICopyRequestHandler handler)
                    {
                        var result = await handler.HandleCopyFileRequestAsync(context.TracingContext, hash);
                        if (result.Succeeded)
                        {
                            return new RequestCopyFileResponse { Header = ResponseHeader.Success(context.StartTime) };
                        }

                        return new RequestCopyFileResponse { Header = ResponseHeader.Failure(context.StartTime, result.ErrorMessage) };

                    }

                    return new RequestCopyFileResponse { Header = ResponseHeader.Failure(context.StartTime, $"No stores implement {nameof(ICopyRequestHandler)}.") };
                },
                (context, errorMessage) =>
                    new RequestCopyFileResponse { Header = ResponseHeader.Failure(context.StartTime, errorMessage) });
        }

        private bool CanHandlePushRequest(Context cacheContext, ContentHash hash, [NotNullWhen(true)]IPushFileHandler store)
        {
            if (store == null)
            {
                Tracer.Debug(cacheContext, $"{nameof(HandlePushFileAsync)}: Copy of {hash.ToShortString()} skipped because no stores implement {nameof(IPushFileHandler)}.");
                return false;
            }

            if (!store.CanAcceptContent(cacheContext, hash, out var rejectionReason))
            {
                Tracer.Debug(cacheContext, $"{nameof(HandlePushFileAsync)}: Copy of {hash.ToShortString()} skipped: {rejectionReason}");
                return false;
            }

            var count = _ongoingPushes.Count;
            if (count >= _ongoingPushCountLimit)
            {
                Tracer.Debug(cacheContext, $"{nameof(HandlePushFileAsync)}: Copy of {hash.ToShortString()} skipped because the max number of proactive pushes of '{_ongoingPushCountLimit}' is reached. OngoingPushes.Count={count}.");
                return false;
            }

            if (!_ongoingPushes.Add(hash))
            {
                Tracer.Debug(cacheContext, $"{nameof(HandlePushFileAsync)}: Copy of {hash.ToShortString()} skipped because another request to push it is already being handled.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Handles a request to copy content to this machine.
        /// </summary>
        public async Task HandlePushFileAsync(IAsyncStreamReader<PushFileRequest> requestStream, IServerStreamWriter<PushFileResponse> responseStream, ServerCallContext callContext)
        {
            // Detaching from the calling thread to (potentially) avoid IO Completion port thread exhaustion
            await Task.Yield();

            var startTime = DateTime.UtcNow;

            var pushRequest = PushRequest.FromMetadata(callContext.RequestHeaders);

            var hash = pushRequest.Hash;
            var cacheContext = new Context(pushRequest.TraceId, Logger);
            var token = callContext.CancellationToken;

            var store = _contentStoreByCacheName.Values.OfType<IPushFileHandler>().FirstOrDefault();

            if (!CanHandlePushRequest(cacheContext, hash, store))
            {
                await callContext.WriteResponseHeadersAsync(PushResponse.DoNotCopy.Metadata);
                return;
            }

            try
            {
                // Running the logic inside try/finally block to remove the hash being processed regardless of the result of this method.
                await callContext.WriteResponseHeadersAsync(PushResponse.Copy.Metadata);

                PutResult? result = null;
                using (var disposableFile = new DisposableFile(cacheContext, _fileSystem, _temporaryDirectory!.CreateRandomFileName()))
                {
                    // NOTE(jubayard): DeleteOnClose not used here because the file needs to be placed into the CAS.
                    // Opening a file for read/write and then doing pretty much anything to it leads to weird behavior
                    // that needs to be tested on a case by case basis. Since we don't know what the underlying store
                    // plans to do with the file, it is more robust to just use the DisposableFile construct.
                    using (var tempFile = await _fileSystem.OpenSafeAsync(disposableFile.Path, FileAccess.Write, FileMode.CreateNew, FileShare.None))
                    {
                        while (await requestStream.MoveNext())
                        {
                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            var request = requestStream.Current;
                            var bytes = request.Content.ToByteArray();
                            await tempFile.WriteAsync(bytes, 0, bytes.Length, token);
                        }
                    }

                    result = await store.HandlePushFileAsync(cacheContext, hash, disposableFile.Path, token);
                }

                var response = result
                    ? new PushFileResponse { Header = ResponseHeader.Success(startTime) }
                    : new PushFileResponse { Header = ResponseHeader.Failure(startTime, result.ErrorMessage) };

                await responseStream.WriteAsync(response);
            }
            finally
            {
                _ongoingPushes.Remove(hash);
            }
        }

        private delegate Task<Result<(long Chunks, long Bytes)>> StreamContentDelegate(Stream input, byte[] buffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct);

        private async Task<Result<(long Chunks, long Bytes)>> StreamContentAsync(Stream input, byte[] buffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct)
        {
            int chunkSize = 0;
            long chunks = 0L;
            long bytes = 0L;

            // Pre-fill buffer with the file's first chunk
            await readNextChunk();

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (chunkSize == 0) { break; }

                ByteString content = ByteString.CopyFrom(buffer, 0, chunkSize);
                CopyFileResponse response = new CopyFileResponse() { Content = content, Index = chunks };

                bytes += chunkSize;
                chunks++;
                // Read the next chunk while waiting for the response
                await Task.WhenAll(readNextChunk(), responseStream.WriteAsync(response));
            }

            return (chunks, bytes);

            async Task<int> readNextChunk() { chunkSize = await input.ReadAsync(buffer, 0, buffer.Length, ct); return chunkSize; }
        }

        private async Task<Result<(long Chunks, long Bytes)>> StreamContentWithCompressionAsync(Stream input, byte[] buffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct)
        {
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
                    await input.CopyToAsync(compressionStream, buffer.Length, ct);
                    await compressionStream.FlushAsync(ct);
                }
                await grpcStream.FlushAsync(ct);
            }

            return (chunks, bytes);
        }

        /// <summary>
        /// Implements a pin request.
        /// </summary>
        private Task<PinResponse> PinAsync(PinRequest request, CancellationToken token)
        {
            return RunFuncAsync(
                request.Header,
                async (context, session) =>
                {
                    PinResult pinResult = await session.PinAsync(
                        context.TracingContext,
                        request.ContentHash.ToContentHash((HashType)request.HashType),
                        token);
                    return new PinResponse
                    {
                        Header = new ResponseHeader(
                                   context.StartTime, pinResult.Succeeded, (int)pinResult.Code, pinResult.ErrorMessage, pinResult.Diagnostics)
                    };
                },
                (context, errorMessage) =>
                    new PinResponse { Header = ResponseHeader.Failure(context.StartTime, (int)PinResult.ResultCode.Error, errorMessage) });
        }

        /// <summary>
        /// Bulk pin content hashes.
        /// </summary>
        private Task<PinBulkResponse> PinBulkAsync(PinBulkRequest request, CancellationToken token)
        {
            return RunFuncAsync(
                request.Header,
                async (context, session) =>
                {
                    var pinList = new List<ContentHash>();
                    foreach (var hash in request.Hashes)
                    {
                        pinList.Add(hash.ContentHash.ToContentHash((HashType)hash.HashType));
                    }

                    List<Task<Indexed<PinResult>>> pinResults = (await session.PinAsync(
                        context.TracingContext,
                        pinList,
                        token)).ToList();
                    var response = new PinBulkResponse();
                    try
                    {
                        foreach (var pinResult in pinResults)
                        {
                            var result = await pinResult;
                            var responseHeader = new ResponseHeader(
                                context.StartTime,
                                result.Item.Succeeded,
                                (int)result.Item.Code,
                                result.Item.ErrorMessage,
                                result.Item.Diagnostics);
                            response.Header.Add(result.Index, responseHeader);
                        }
                    }
                    catch (Exception)
                    {
                        pinResults.ForEach(task => task.FireAndForget(context.TracingContext));
                        throw;
                    }

                    return response;
                },
                (context, errorMessage) =>
                {
                    var header = ResponseHeader.Failure(context.StartTime, (int)PinResult.ResultCode.Error, errorMessage);
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
        private Task<PlaceFileResponse> PlaceFileAsync(PlaceFileRequest request, CancellationToken token)
        {
            return RunFuncAsync(
                request.Header,
                async (context, session) =>
                {
                    PlaceFileResult placeFileResult = await session.PlaceFileAsync(
                        context.TracingContext,
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
                                       context.StartTime,
                                       placeFileResult.Succeeded,
                                       (int)placeFileResult.Code,
                                       placeFileResult.ErrorMessage,
                                       placeFileResult.Diagnostics),
                        ContentSize = placeFileResult.FileSize
                    };
                },
                (context, errorMessage) => new PlaceFileResponse
                {
                    Header = ResponseHeader.Failure(context.StartTime, (int)PlaceFileResult.ResultCode.Error, errorMessage)
                });
        }

        /// <summary>
        /// Implements a put file request.
        /// </summary>
        private Task<PutFileResponse> PutFileAsync(PutFileRequest request, CancellationToken token)
        {
            return RunFuncAsync(
                request.Header,
                async (context, session) =>
                {
                    PutResult putResult;
                    if (request.ContentHash == ByteString.Empty)
                    {
                        putResult = await session.PutFileAsync(
                            context.TracingContext,
                            (HashType)request.HashType,
                            new AbsolutePath(request.Path),
                            (FileRealizationMode)request.FileRealizationMode,
                            token);
                    }
                    else
                    {
                        putResult = await session.PutFileAsync(
                            context.TracingContext,
                            request.ContentHash.ToContentHash((HashType)request.HashType),
                            new AbsolutePath(request.Path),
                            (FileRealizationMode)request.FileRealizationMode,
                            token);
                    }

                    return new PutFileResponse
                    {
                        Header =
                                   new ResponseHeader(
                                       context.StartTime,
                                       putResult.Succeeded,
                                       putResult.Succeeded ? 0 : 1,
                                       putResult.ErrorMessage,
                                       putResult.Diagnostics),
                        ContentSize = putResult.ContentSize,
                        ContentHash = putResult.ContentHash.ToByteString(),
                        HashType = (int)putResult.ContentHash.HashType
                    };
                },
                (context, errorMessage) => new PutFileResponse { Header = ResponseHeader.Failure(context.StartTime, errorMessage) });
        }

        /// <summary>
        /// Implements a heartbeat request for a session.
        /// </summary>
        private Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken token)
        {
            return RunFuncAsync(
                request.Header,
                (context, session) => Task.FromResult(new HeartbeatResponse { Header = ResponseHeader.Success(context.StartTime) }),
                (context, errorMessage) => new HeartbeatResponse { Header = ResponseHeader.Failure(context.StartTime, errorMessage) },
                // It is important to trace heartbeat messages because lack of them will cause sessions to expire.
                trace: true);
        }

        private async Task<DeleteContentResponse> DeleteAsync(DeleteContentRequest request, CancellationToken ct)
        {
            return await RunFuncNoSessionAsync(
                request.TraceId,
                async context =>
                {
                    var contentHash = request.ContentHash.ToContentHash((HashType)request.HashType);

                    var deleteOptions = new DeleteContentOptions() { DeleteLocalOnly = request.DeleteLocalOnly };
                    var deleteResults = await Task.WhenAll<DeleteResult>(_contentStoreByCacheName.Values.Select(store => store.DeleteAsync(context.TracingContext, contentHash, deleteOptions)));

                    bool succeeded = true;
                    long contentSize = 0L;
                    int code = (int)DeleteResult.ResultCode.ContentNotFound;
                    var response = new DeleteContentResponse();
                    foreach (var deleteResult in deleteResults)
                    {
                        if (deleteOptions.DeleteLocalOnly)
                        {
                            succeeded &= deleteResult.Succeeded;

                            // Return the most severe result code
                            code = Math.Max(code, (int)deleteResult.Code);
                        }
                        else
                        {
                            if (deleteResult is DistributedDeleteResult distributedDeleteResult)
                            {
                                foreach (var kvp in distributedDeleteResult.DeleteMapping)
                                {
                                    response.DeleteResults.Add(kvp.Key, new ResponseHeader(context.StartTime, kvp.Value.Succeeded, (int)kvp.Value.Code, kvp.Value.ErrorMessage, kvp.Value.Diagnostics));
                                }
                            }
                        }

                        contentSize = Math.Max(deleteResult.ContentSize, contentSize);
                    }

                    response.Header = succeeded ? ResponseHeader.Success(context.StartTime) : ResponseHeader.Failure(context.StartTime, string.Join(Environment.NewLine, deleteResults.Select(r => r.ToString())));
                    response.ContentSize = contentSize;
                    response.Result = code;
                    return response;
                },
                (context, errorMessage) => new DeleteContentResponse() {Header = ResponseHeader.Failure(context.StartTime, errorMessage)}
                );
        }

        private struct RequestContext
        {
            public RequestContext(DateTime startTime, Context tracingContext)
            {
                StartTime = startTime;
                TracingContext = tracingContext;
            }

            public DateTime StartTime { get; }
            public Context TracingContext { get; }
        }

        private async Task<T> RunFuncAsync<T>(
            RequestHeader header,
            Func<RequestContext, IContentSession, Task<T>> taskFunc,
            Func<RequestContext, string, T> failFunc,
            bool trace = false,
            bool obtainSession = true,
            [CallerMemberName]string? caller = null)
        {
            var context = new RequestContext(startTime: DateTime.UtcNow, new Context(Guid.Parse(header.TraceId), Logger));
            int sessionId = header.SessionId;

            IContentSession? session = null;

            if (obtainSession && !ContentSessionHandler.TryGetSession(sessionId, out session))
            {
                string message = $"Could not find session for session ID {sessionId}";
                Logger.Info(message);
                return failFunc(context, message);
            }

            var sw = StopwatchSlim.Start();

            // Detaching from the calling thread to (potentially) avoid IO Completion port thread exhaustion
            await Task.Yield();

            try
            {
                if (trace)
                {
                    context.TracingContext.Debug($"Starting GRPC operation {caller} for session {sessionId}.");
                }

                var result = await taskFunc(context, session!);

                if (trace)
                {
                    context.TracingContext.Debug($"GRPC operation {caller} is finished in {sw.Elapsed.TotalMilliseconds}ms for session {sessionId}.");
                }

                return result;
            }
            catch (TaskCanceledException)
            {
                var message = $"The GRPC server operation {caller} was canceled in {sw.Elapsed.TotalMilliseconds}ms.";
                context.TracingContext.Info(message);
                return failFunc(context, message);
            }
            catch (Exception e)
            {
                context.TracingContext.Error($"GRPC server operation {caller} failed in {sw.Elapsed.TotalMilliseconds}ms. {e}");
                return failFunc(context, e.ToString());
            }
        }

        private Task<T> RunFuncNoSessionAsync<T>(
            string traceId,
            Func<RequestContext, Task<T>> taskFunc,
            Func<RequestContext, string, T> failFunc)
        {
            return RunFuncAsync(
                new RequestHeader(traceId, sessionId: -1), 
                (context, _) => taskFunc(context),
                failFunc,
                obtainSession: false);
        }

        /// <summary>
        /// Glue logic between this class and the Grpc abstract class.
        /// </summary>
        /// <remarks>
        /// This adapter only implements the content verbs, and will throw an
        /// unimplemented exception when a client calls an unavailable method.
        /// </remarks>
        protected class ContentServerAdapter : ContentServer.ContentServerBase
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
            public override Task<RequestCopyFileResponse> RequestCopyFile(RequestCopyFileRequest request, ServerCallContext context) => _contentServer.RequestCopyFileAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task PushFile(IAsyncStreamReader<PushFileRequest> requestStream, IServerStreamWriter<PushFileResponse> responseStream, ServerCallContext context) => _contentServer.HandlePushFileAsync(requestStream, responseStream, context);

            /// <inheritdoc />
            public override Task<HelloResponse> Hello(HelloRequest request, ServerCallContext context) => _contentServer.HelloAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<GetStatsResponse> GetStats(GetStatsRequest request, ServerCallContext context) => _contentServer.GetStatsAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<CreateSessionResponse> CreateSession(CreateSessionRequest request, ServerCallContext context) => _contentServer.CreateSessionAsync(request, context.CancellationToken);

            /// <inheritdoc />
            public override Task<DeleteContentResponse> Delete(DeleteContentRequest request, ServerCallContext context) => _contentServer.DeleteAsync(request, context.CancellationToken);

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
