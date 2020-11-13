// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using ContentStore.Grpc;
using Google.Protobuf;
using Grpc.Core;
using PinRequest = ContentStore.Grpc.PinRequest;
using static BuildXL.Utilities.ConfigurationHelper;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <nodoc />
    public enum GrpcContentServerCounters
    {
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        StreamContentReadFromDiskDuration,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        CopyStreamContentDuration,

        /// <nodoc />
        CopyRequestRejected,

        /// <nodoc />
        CopyContentNotFound,

        /// <nodoc />
        CopyError,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HandlePushFile,

        /// <nodoc />
        PushFileRejectNotSupported,

        /// <nodoc />
        PushFileRejectCopyLimitReached,

        /// <nodoc />
        PushFileRejectCopyOngoingCopy,
    }

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

        /// <nodoc />
        public CounterCollection<GrpcContentServerCounters> Counters { get; } = new CounterCollection<GrpcContentServerCounters>();

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

        private readonly ConcurrencyLimiter<Guid> _copyFromConcurrencyLimiter;

        private readonly ConcurrencyLimiter<ContentHash> _ongoingPushesConcurrencyLimiter;


        private readonly bool _traceGrpcOperations;

        /// <nodoc />
        public GrpcContentServer(
            ILogger logger,
            Capabilities serviceCapabilities,
            ISessionHandler<IContentSession> sessionHandler,
            IReadOnlyDictionary<string, IContentStore> storesByName,
            LocalServerConfiguration? localServerConfiguration = null)
        {
            Contract.Requires(storesByName != null);

            _serviceCapabilities = serviceCapabilities;
            _contentStoreByCacheName = storesByName;
            _bufferSize = localServerConfiguration?.BufferSizeForGrpcCopies ?? ContentStore.Grpc.GrpcConstants.DefaultBufferSizeBytes;
            _gzipSizeBarrier = localServerConfiguration?.GzipBarrierSizeForGrpcCopies ?? (_bufferSize * 8);
            _traceGrpcOperations = localServerConfiguration?.TraceGrpcOperations ?? false;
            _ongoingPushesConcurrencyLimiter = new ConcurrencyLimiter<ContentHash>(localServerConfiguration?.ProactivePushCountLimit ?? LocalServerConfiguration.DefaultProactivePushCountLimit);
            _copyFromConcurrencyLimiter = new ConcurrencyLimiter<Guid>(localServerConfiguration?.CopyRequestHandlingCountLimit ?? LocalServerConfiguration.DefaultCopyRequestHandlingCountLimit);
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
                    TempDirectory = sessionCreationResult.Value.tempDirectory?.Path
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

            var result = counters.Value!;
            result.Merge(Counters.ToCounterSet(), "GrpcContentServer");
            return GetStatsResponse.Create(result.ToDictionaryIntegral());
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
            using var shutdownTracker = TrackShutdown(cacheContext, token);

            var removeFromTrackerResult = await ContentSessionHandler.RemoveFromTrackerAsync(shutdownTracker.Context);
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

        private CopyLimiter CanHandleCopyRequest(bool respectConcurrencyLimit)
        {
            var operationId = Guid.NewGuid();
            var (_, overTheLimit) = _copyFromConcurrencyLimiter.TryAdd(operationId, respectTheLimit: respectConcurrencyLimit);

            return new CopyLimiter(_copyFromConcurrencyLimiter, operationId, overTheLimit);
        }

        private Task SendErrorResponseAsync(ServerCallContext context, string errorType, string errorMessage)
        {
            Metadata headers = new Metadata();
            headers.Add("Exception", errorType);
            headers.Add("Message", errorMessage);
            return context.WriteResponseHeadersAsync(headers);
        }

        private async Task HandleCopyRequestAsync(CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext callContext)
        {
            var cacheContext = new Context(new Guid(request.TraceId), Logger);
            var operationContext = new OperationContext(cacheContext);

            var result = await operationContext
                .PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        await HandleCopyRequestCoreAsync(cacheContext, request, responseStream, callContext);
                        return BoolResult.Success;
                    },
                    traceErrorsOnly: true);

            if (!result.Succeeded)
            {
                var exception = result.Exception!;
                await SendErrorResponseAsync(callContext, exception.GetType().Name, exception.Message);
            }
        }

        private async Task HandleCopyRequestCoreAsync(Context cacheContext, CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext callContext)
        {
            using var limiter = CanHandleCopyRequest(request.FailFastIfBusy);

            if (limiter.OverTheLimit)
            {
                Counters[GrpcContentServerCounters.CopyRequestRejected].Increment();
                await SendErrorResponseAsync(
                    callContext,
                    errorType: "ConcurrencyLimitReached",
                    errorMessage: $"Copy limit of '{limiter.Limit}' reached. Current number of pending copies: {limiter.CurrentCount}");
                return;
            }

            ContentHash hash = request.GetContentHash();
            long size = -1;
            OpenStreamResult result = await GetFileStreamAsync(cacheContext, hash);

            // If result is unsuccessful, then result.Stream is null, but using(null) is just a no op.
            using (result.Stream)
            {
                // Figure out response headers.
                CopyCompression compression = CopyCompression.None;
                
                switch (result.Code)
                {
                    case OpenStreamResult.ResultCode.ContentNotFound:
                        Counters[GrpcContentServerCounters.CopyContentNotFound].Increment();
                        await SendErrorResponseAsync(
                            callContext,
                            errorType: "ContentNotFound",
                            errorMessage: $"Requested content at {hash.ToString()} not found.");
                        break;
                    case OpenStreamResult.ResultCode.Error:
                        Contract.Assert(result.Exception != null);
                        Counters[GrpcContentServerCounters.CopyError].Increment();
                        await SendErrorResponseAsync(
                            callContext,
                            errorType: result.Exception.GetType().Name,
                            errorMessage: result.Exception.Message);
                        break;
                    case OpenStreamResult.ResultCode.Success:
                        Contract.Assert(result.Stream != null);
                        size = result.Stream.Length;
                        Metadata headers = new Metadata();
                        headers.Add("FileSize", size.ToString());
                        if ((request.Compression == CopyCompression.Gzip) && (size > _gzipSizeBarrier))
                        {
                            compression = CopyCompression.Gzip;
                        }

                        headers.Add("Compression", compression.ToString());
                        headers.Add("ChunkSize", _bufferSize.ToString());
                        // Send the response headers.
                        await callContext.WriteResponseHeadersAsync(headers);
                        break;
                    default:
                        throw new NotImplementedException($"Unknown result.Code '{result.Code}'.");
                }

                // Send the content.
                if (result.Succeeded)
                {
                    // Need to cancel the operation when the instance is shut down.
                    using var shutdownTracker = TrackShutdown(cacheContext, callContext.CancellationToken);
                    var operationContext = shutdownTracker.Context;

                    using var arrayHandle = _pool.Get();
                    using var arraySecondaryHandle = _pool.Get();
                    StreamContentDelegate streamContent = compression == CopyCompression.None ? (StreamContentDelegate)StreamContentAsync : StreamContentWithCompressionAsync;

                    byte[] buffer = arrayHandle.Value;
                    byte[] secondaryBuffer = arraySecondaryHandle.Value;
                    (await operationContext.PerformOperationAsync(
                            _tracer,
                            () => streamContent(result.Stream!, buffer, secondaryBuffer, responseStream, operationContext.Token),
                            traceOperationStarted: false, // Tracing only stop messages
                            extraEndMessage: r => $"Hash={hash.ToShortString()}, GZip={(compression == CopyCompression.Gzip ? "on" : "off")}, Size=[{size}], Sender=[{GetSender(callContext)}], ReadTime=[{getFileIoDurationAsString()}].",
                            counter: Counters[GrpcContentServerCounters.CopyStreamContentDuration])
                        ).Then((r, duration) => trackMetrics(r.totalChunksCount, r.totalBytes, duration))
                        .IgnoreFailure(); // The error was already logged.

                    BoolResult trackMetrics(long totalChunksCount, long totalBytes, TimeSpan duration)
                    {
                        Tracer.TrackMetric(operationContext, "HandleCopyFile_TotalChunksCount", totalChunksCount);
                        Tracer.TrackMetric(operationContext, "HandleCopyFile_TotalBytes", totalBytes);
                        Tracer.TrackMetric(operationContext, "HandleCopyFile_StreamDurationMs", (long)duration.TotalMilliseconds);
                        Tracer.TrackMetric(operationContext, "HandleCopyFile_OpenStreamDurationMs", result.DurationMs);

                        ApplyIfNotNull(GetFileIODuration(result.Stream), d => Tracer.TrackMetric(operationContext, "HandleCopyFile_FileIoDurationMs", (long)d.TotalMilliseconds));
                        return BoolResult.Success;
                    }

                    string getFileIoDurationAsString()
                    {
                        var duration = GetFileIODuration(result.Stream);
                        return duration != null ? $"{(long)duration.Value.TotalMilliseconds}ms" : string.Empty;
                    }
                }
            }
        }

        private static string? GetSender(ServerCallContext context)
        {
            if (context.RequestHeaders.TryGetCallingMachineName(out var result))
            {
                return result;
            }

            return context.Host;
        }

        private TimeSpan? GetFileIODuration(Stream? resultStream)
        {
            if (resultStream is TrackingFileStream tfs)
            {
                Counters.AddToCounter(GrpcContentServerCounters.StreamContentReadFromDiskDuration, tfs.ReadDuration);
                return tfs.ReadDuration;
            }

            return null;
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
                        var result = await handler.HandleCopyFileRequestAsync(context.OperationContext, hash, context.Token);
                        if (result.Succeeded)
                        {
                            return new RequestCopyFileResponse { Header = ResponseHeader.Success(context.StartTime) };
                        }

                        return new RequestCopyFileResponse { Header = ResponseHeader.Failure(context.StartTime, result.ErrorMessage) };

                    }

                    return new RequestCopyFileResponse { Header = ResponseHeader.Failure(context.StartTime, $"No stores implement {nameof(ICopyRequestHandler)}.") };
                },
                (context, errorMessage) =>
                    new RequestCopyFileResponse { Header = ResponseHeader.Failure(context.StartTime, errorMessage) },
                cancellationToken);
        }

        /// <summary>
        /// Handles a request to copy content to this machine.
        /// </summary>
        public async Task HandlePushFileAsync(IAsyncStreamReader<PushFileRequest> requestStream, IServerStreamWriter<PushFileResponse> responseStream, ServerCallContext callContext)
        {
            // Detaching from the calling thread to (potentially) avoid IO Completion port thread exhaustion
            await Task.Yield();

            var pushRequest = PushRequest.FromMetadata(callContext.RequestHeaders);
            var cacheContext = new OperationContext(new Context(pushRequest.TraceId, Logger));

            // Using PerformOperation to return the information about any potential errors happen in this code.
            var result = await cacheContext.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    await HandlePushFileCoreAsync(cacheContext, pushRequest, requestStream, responseStream, callContext);
                    return BoolResult.Success;
                },
                traceErrorsOnly: true);

            if (!result)
            {
                var exception = result.Exception!;
                await SendErrorResponseAsync(callContext, exception.GetType().Name, exception.ToString());
            }
        }

        private async Task HandlePushFileCoreAsync(Context cacheContext, PushRequest pushRequest, IAsyncStreamReader<PushFileRequest> requestStream, IServerStreamWriter<PushFileResponse> responseStream, ServerCallContext callContext)
        {
            var startTime = DateTime.UtcNow;
            var hash = pushRequest.Hash;

            // Cancelling the operation when the instance is shut down.
            using var shutdownTracker = TrackShutdown(cacheContext, callContext.CancellationToken);
            var token = shutdownTracker.Context.Token;

            var store = _contentStoreByCacheName.Values.OfType<IPushFileHandler>().FirstOrDefault();

            using var limiter = PushCopyLimiter.Create(cacheContext, _ongoingPushesConcurrencyLimiter, hash, store);
            if (limiter.RejectionReason != RejectionReason.Accepted)
            {
                var rejectCounter = limiter.RejectCounter;
                if (rejectCounter != null)
                {
                    Counters[rejectCounter.Value].Increment();
                }

                Tracer.Debug(cacheContext, $"{nameof(HandlePushFileCoreAsync)}: Copy is skipped. Hash={hash.ToShortString()}, Reason={limiter.RejectionReason}, Limit={limiter.Limit}, CurrentCount={limiter.CurrentCount}.");
                await callContext.WriteResponseHeadersAsync(PushResponse.DoNotCopy(limiter.RejectionReason).Metadata);
                return;
            }

            var operationContext = new OperationContext(cacheContext, token);
            await operationContext.PerformNonResultOperationAsync(
                Tracer,
                async () =>
                {
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
                            // From the docs: On the server side, MoveNext() does not throw exceptions.
                            // In case of a failure, the request stream will appear to be finished (MoveNext will return false)
                            // and the CancellationToken associated with the call will be cancelled to signal the failure.
                            await GrpcExtensions.CopyChunksToStreamAsync(requestStream, tempFile.Stream, request => request.Content, cancellationToken: token);
                        }

                        if (token.IsCancellationRequested)
                        {
                            if (!callContext.CancellationToken.IsCancellationRequested)
                            {
                                await responseStream.WriteAsync(new PushFileResponse()
                                {
                                    Header = ResponseHeader.Failure(startTime, "Operation cancelled by handler.")
                                });
                            }

                            var cancellationSource = callContext.CancellationToken.IsCancellationRequested ? "caller" : "handler";
                            cacheContext.Debug($"{nameof(HandlePushFileAsync)}: Copy of {hash.ToShortString()} cancelled by {cancellationSource}.");
                            return Unit.Void;
                        }

                        Contract.Assert(store != null);
                        result = await store.HandlePushFileAsync(cacheContext, hash, disposableFile.Path, token);
                    }

                    var response = result
                        ? new PushFileResponse { Header = ResponseHeader.Success(startTime) }
                        : new PushFileResponse { Header = ResponseHeader.Failure(startTime, result.ErrorMessage) };

                    await responseStream.WriteAsync(response);
                    return Unit.Void;
                },
                traceErrorsOnly: true,
                counter: Counters[GrpcContentServerCounters.HandlePushFile]);
        }

        private delegate Task<Result<(long totalChunksCount, long totalBytes)>> StreamContentDelegate(Stream input, byte[] buffer, byte[] secondaryBuffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct);

        private async Task<Result<(long totalChunksCount, long totalBytes)>> StreamContentAsync(Stream input, byte[] buffer, byte[] secondaryBuffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct)
        {
            return await GrpcExtensions.CopyStreamToChunksAsync(input, responseStream, (content, chunks) => new CopyFileResponse()
            {
                Content = content,
                Index = chunks,
            }, buffer, secondaryBuffer, cancellationToken: ct);
        }

        private async Task<Result<(long totalChunksCount, long totalBytes)>> StreamContentWithCompressionAsync(Stream input, byte[] buffer, byte[] secondaryBuffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct)
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
                        context.OperationContext,
                        request.ContentHash.ToContentHash((HashType)request.HashType),
                        context.Token);
                    return new PinResponse
                    {
                        Header = new ResponseHeader(
                                   context.StartTime, pinResult.Succeeded, (int)pinResult.Code, pinResult.ErrorMessage, pinResult.Diagnostics),
                        Info = GetResponseInfo(pinResult)
                    };
                },
                (context, errorMessage) =>
                    new PinResponse { Header = ResponseHeader.Failure(context.StartTime, (int)PinResult.ResultCode.Error, errorMessage) },
                token);
        }

        private PinResponseInfo GetResponseInfo(PinResult result)
        {
            if (!result.Succeeded)
            {
                return new PinResponseInfo();
            }

            return new PinResponseInfo()
            {
                ContentSize = result.ContentSize
            };
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
                        context.OperationContext,
                        pinList,
                        context.Token)).ToList();
                    var response = new PinBulkResponse();
                    try
                    {
                        PinResponseInfo?[] info = new PinResponseInfo[pinList.Count];

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
                            info[result.Index] = GetResponseInfo(result.Item);
                        }

                        response.Info.AddRange(info);
                    }
                    catch (Exception)
                    {
                        pinResults.ForEach(task => task.FireAndForget(context.OperationContext));
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
                },
                token);
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
                        context.OperationContext,
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
                },
                token);
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
                            context.OperationContext,
                            (HashType)request.HashType,
                            new AbsolutePath(request.Path),
                            (FileRealizationMode)request.FileRealizationMode,
                            context.Token);
                    }
                    else
                    {
                        putResult = await session.PutFileAsync(
                            context.OperationContext,
                            request.ContentHash.ToContentHash((HashType)request.HashType),
                            new AbsolutePath(request.Path),
                            (FileRealizationMode)request.FileRealizationMode,
                            context.Token);
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
                (context, errorMessage) => new PutFileResponse { Header = ResponseHeader.Failure(context.StartTime, errorMessage) },
                token: token);
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
                token,
                // It is important to trace heartbeat messages because lack of them will cause sessions to expire.
                traceStartAndStop: true);
        }

        private async Task<DeleteContentResponse> DeleteAsync(DeleteContentRequest request, CancellationToken ct)
        {
            return await RunFuncNoSessionAsync(
                request.TraceId,
                async context =>
                {
                    var contentHash = request.ContentHash.ToContentHash((HashType)request.HashType);

                    var deleteOptions = new DeleteContentOptions() { DeleteLocalOnly = request.DeleteLocalOnly };
                    var deleteResults = await Task.WhenAll<DeleteResult>(_contentStoreByCacheName.Values.Select(store => store.DeleteAsync(context.OperationContext, contentHash, deleteOptions)));

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
                (context, errorMessage) => new DeleteContentResponse() { Header = ResponseHeader.Failure(context.StartTime, errorMessage) },
                token: ct
                );
        }

        private readonly struct RequestContext
        {
            public RequestContext(DateTime startTime, OperationContext tracingContext)
            {
                StartTime = startTime;
                OperationContext = tracingContext;
            }

            public DateTime StartTime { get; }
            public OperationContext OperationContext { get; }
            public CancellationToken Token => OperationContext.Token;
        }

        private async Task<T> RunFuncAsync<T>(
            RequestHeader header,
            Func<RequestContext, IContentSession, Task<T>> taskFunc,
            Func<RequestContext, string, T> failFunc,
            CancellationToken token,
            bool? traceStartAndStop = null,
            bool obtainSession = true,
            [CallerMemberName] string? caller = null)
        {
            bool trace = traceStartAndStop ?? _traceGrpcOperations;

            var tracingContext = new Context(Guid.Parse(header.TraceId), Logger);
            using var shutdownTracker = TrackShutdown(tracingContext, token);

            var context = new RequestContext(startTime: DateTime.UtcNow, shutdownTracker.Context);
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
                    Tracer.Debug(tracingContext, $"Starting GRPC operation {caller} for session {sessionId}.");
                }

                var result = await taskFunc(context, session!);

                if (trace)
                {
                    Tracer.Debug(tracingContext, $"GRPC operation {caller} is finished in {sw.Elapsed.TotalMilliseconds}ms for session {sessionId}.");
                }

                return result;
            }
            catch (TaskCanceledException)
            {
                var message = $"The GRPC server operation {caller} was canceled in {sw.Elapsed.TotalMilliseconds}ms.";
                Tracer.Info(tracingContext, message);
                return failFunc(context, message);
            }
            catch (Exception e)
            {
                Tracer.Error(tracingContext, $"GRPC server operation {caller} failed in {sw.Elapsed.TotalMilliseconds}ms. {e}");
                return failFunc(context, e.ToString());
            }
        }

        private Task<T> RunFuncNoSessionAsync<T>(
            string traceId,
            Func<RequestContext, Task<T>> taskFunc,
            Func<RequestContext, string, T> failFunc,
            CancellationToken token)
        {
            return RunFuncAsync(
                new RequestHeader(traceId, sessionId: -1),
                (context, _) => taskFunc(context),
                failFunc,
                token,
                obtainSession: false);
        }

        /// <summary>
        /// A helper struct for limiting the number of concurrent copy operations.
        /// </summary>
        private readonly struct CopyLimiter : IDisposable
        {
            private readonly ConcurrencyLimiter<Guid> _limiter;
            private readonly Guid _operationId;

            public bool OverTheLimit { get; }

            public int CurrentCount => _limiter.Count;

            public int Limit => _limiter.Limit;

            public CopyLimiter(ConcurrencyLimiter<Guid> limiter, Guid operationId, bool overTheLimit)
            {
                _limiter = limiter;
                _operationId = operationId;
                OverTheLimit = overTheLimit;
            }

            public void Dispose()
            {
                _limiter.Remove(_operationId);
            }
        }

        /// <summary>
        /// A helper struct for limiting the number of concurrent push operations.
        /// </summary>
        private readonly struct PushCopyLimiter : IDisposable
        {
            private readonly ConcurrencyLimiter<ContentHash> _limiter;
            private readonly ContentHash _contentHash;

            public int CurrentCount => _limiter.Count;

            public int Limit => _limiter.Limit;
            public RejectionReason RejectionReason { get; }

            public string RejectionDescription => RejectionReason switch
            {
                RejectionReason.Accepted => "Accepted",
                RejectionReason.NotSupported => $"No stores implement {nameof(IPushFileHandler)}",
                RejectionReason.CopyLimitReached => $"The max number of proactive pushes of {Limit} is reached. OngoingPushes.Count={CurrentCount}",
                RejectionReason.OngoingCopy => $"Another request to push it is already being handled",
                _ => string.Empty
            };

            public GrpcContentServerCounters? RejectCounter =>
                RejectionReason switch
                {
                    RejectionReason.NotSupported => GrpcContentServerCounters.PushFileRejectNotSupported,
                    RejectionReason.CopyLimitReached => GrpcContentServerCounters.PushFileRejectCopyLimitReached,
                    RejectionReason.OngoingCopy => GrpcContentServerCounters.PushFileRejectCopyOngoingCopy,
                    _ => null,
                };

            public PushCopyLimiter(ConcurrencyLimiter<ContentHash> limiter, ContentHash contentHash, RejectionReason rejectionReason)
            {
                RejectionReason = rejectionReason;
                _limiter = limiter;
                _contentHash = contentHash;
            }

            public static PushCopyLimiter Create(Context context, ConcurrencyLimiter<ContentHash> limiter, ContentHash hash, IPushFileHandler? store)
            {
                var (added, overTheLimit) = limiter.TryAdd(hash, respectTheLimit: true);

                if (store == null)
                {
                    return new PushCopyLimiter(limiter, hash, RejectionReason.NotSupported);
                }

                if (!store.CanAcceptContent(context, hash, out var rejectionReason))
                {
                    return new PushCopyLimiter(limiter, hash, rejectionReason);
                }
                
                if (overTheLimit)
                {
                    return new PushCopyLimiter(limiter, hash, RejectionReason.CopyLimitReached);
                }

                if (!added)
                {
                    return new PushCopyLimiter(limiter, hash, RejectionReason.OngoingCopy);
                }

                return new PushCopyLimiter(limiter, hash, RejectionReason.Accepted);
            }

            public void Dispose()
            {
                _limiter.Remove(_contentHash);
            }
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
            public override Task CopyFile(CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext context) => _contentServer.HandleCopyRequestAsync(request, responseStream, context);

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
