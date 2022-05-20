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
using BuildXL.Utilities.Tracing;
using ContentStore.Grpc;
using Google.Protobuf;
using Grpc.Core;
using PinRequest = ContentStore.Grpc.PinRequest;
using BuildXL.Cache.Host.Service;

using static BuildXL.Utilities.ConfigurationHelper;
using CompressionLevel = System.IO.Compression.CompressionLevel;

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
        [CounterType(CounterType.Stopwatch)]
        HandleCopyFile,

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
    public class GrpcContentServer : StartupShutdownSlimBase, IDistributedStreamStore, IGrpcServiceEndpoint
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(GrpcContentServer));

        private readonly Capabilities _serviceCapabilities;
        private readonly IReadOnlyDictionary<string, IContentStore> _contentStoreByCacheName;
        private readonly int _bufferSize;
        private readonly ByteArrayPool _pool;

        private readonly IAbsFileSystem _fileSystem;
        private readonly AbsolutePath _workingDirectory;
        private DisposableDirectory? _temporaryDirectory;

        private readonly IColdStorage? _coldStorage;

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
        /// This is a hack to allow for an <see cref="ISessionHandler{TSession, TSessionData}"/> with other sessions that inherit from
        /// <see cref="IContentSession"/> with session data which inherits from <see cref="LocalContentServerSessionData"/> to be used instead.
        /// </remarks>
        protected virtual ISessionHandler<IContentSession, LocalContentServerSessionData> ContentSessionHandler { get; }

        /// <inheritdoc />
        public IPushFileHandler? PushFileHandler { get; }

        /// <nodoc />
        public ICopyRequestHandler? CopyRequestHandler { get; }

        /// <inheritdoc />
        public IDistributedStreamStore StreamStore => this;

        /// <summary>
        /// [Used for testing only]: an exception that will be thrown during copy files or handling push files.
        /// </summary>
        internal Exception? HandleRequestFailure { get; set; }

        private readonly ConcurrencyLimiter<Guid> _copyFromConcurrencyLimiter;

        private readonly ConcurrencyLimiter<ContentHash> _ongoingPushesConcurrencyLimiter;

        /// <summary>
        /// If true, then all grpc-level operation should emit start and stop messages.
        /// </summary>
        protected readonly bool TraceGrpcOperations;

        /// <nodoc />
        public GrpcContentServer(
            ILogger logger,
            Capabilities serviceCapabilities,
            ISessionHandler<IContentSession, LocalContentServerSessionData> sessionHandler,
            IReadOnlyDictionary<string, IContentStore> storesByName,
            LocalServerConfiguration? localServerConfiguration = null,
            IColdStorage? coldStorage = null)
        {
            Contract.Requires(storesByName != null);

            _serviceCapabilities = serviceCapabilities;
            _contentStoreByCacheName = storesByName;
            _bufferSize = localServerConfiguration?.BufferSizeForGrpcCopies ?? ContentStore.Grpc.GrpcConstants.DefaultBufferSizeBytes;
            TraceGrpcOperations = localServerConfiguration?.TraceGrpcOperations ?? false;
            _ongoingPushesConcurrencyLimiter = new ConcurrencyLimiter<ContentHash>(localServerConfiguration?.ProactivePushCountLimit ?? LocalServerConfiguration.DefaultProactivePushCountLimit);
            _copyFromConcurrencyLimiter = new ConcurrencyLimiter<Guid>(localServerConfiguration?.CopyRequestHandlingCountLimit ?? LocalServerConfiguration.DefaultCopyRequestHandlingCountLimit);
            _pool = new ByteArrayPool(_bufferSize);
            ContentSessionHandler = sessionHandler;

            _fileSystem = localServerConfiguration?.FileSystem ?? new PassThroughFileSystem();
            _workingDirectory = (localServerConfiguration?.DataRootPath ?? _fileSystem.GetTempPath()) / "GrpcContentServer";

            GrpcAdapter = new ContentServerAdapter(this);
            PushFileHandler = storesByName.Values.OfType<IPushFileHandler>().FirstOrDefault();
            CopyRequestHandler = _contentStoreByCacheName.Values.OfType<ICopyRequestHandler>().FirstOrDefault();

            _coldStorage = coldStorage;

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

        /// <inheritdoc />
        public void BindServices(Server.ServiceDefinitionCollection services)
        {
            services.Add(ContentServer.BindService(GrpcAdapter));
        }

        /// <inheritdoc />
        public void MapServices(IGrpcServiceEndpointCollection endpoints)
        {
            endpoints.MapService<ContentServerAdapter>();
        }

        /// <inheritdoc />
        public void AddServices(IGrpcServiceCollection services)
        {
            services.AddService(GrpcAdapter);
        }

        /// <summary>
        /// Implements a create session request.
        /// </summary>
        public virtual Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request, CancellationToken token)
            => CreateSessionAsync(request.TraceId, request.SessionName, request.CacheName, request.ImplicitPin, request.Capabilities, token);

        /// <nodoc />
        protected async Task<CreateSessionResponse> CreateSessionAsync(
            string traceId,
            string sessionName,
            string cacheName,
            int implicitPin,
            int capabilities,
            CancellationToken token)
        {
            var cacheContext = new Context(traceId, Logger);

            var sessionData = new LocalContentServerSessionData(sessionName, (Capabilities)capabilities, (ImplicitPin)implicitPin, pins: Array.Empty<string>());

            var sessionCreationResult = await ContentSessionHandler.CreateSessionAsync(
                new OperationContext(cacheContext, token),
                sessionData,
                cacheName);

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
            var cacheContext = new Context(request.Header.TraceId, Logger);
            await ContentSessionHandler.ReleaseSessionAsync(new OperationContext(cacheContext, token), request.Header.SessionId);
            return new ShutdownResponse();
        }

        /// <nodoc />
#pragma warning disable IDE0060 // Remove unused parameter
        private Task<HelloResponse> HelloAsync(HelloRequest request, CancellationToken token)
#pragma warning restore IDE0060 // Remove unused parameter
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
            var cacheContext = new Context(Logger);
            var counters = await ContentSessionHandler.GetStatsAsync(new OperationContext(cacheContext, token));
            if (!counters.Succeeded)
            {
                return GetStatsResponse.Failure();
            }

            var result = counters.Value;
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
            var cacheContext = new Context(request.TraceId, Logger);
            using var shutdownTracker = TrackShutdown(cacheContext, token);

            var removeFromTrackerResult = await ContentSessionHandler.RemoveFromTrackerAsync(shutdownTracker.Context);
            if (!removeFromTrackerResult)
            {
                return new RemoveFromTrackerResponse
                {
                    Header = ResponseHeader.Failure(startTime, removeFromTrackerResult.ErrorMessage, removeFromTrackerResult.Diagnostics)
                };
            }

            return new RemoveFromTrackerResponse
            {
                Header = ResponseHeader.Success(startTime),
                FilesEvicted = 0
            };
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> OpenStreamAsync(OperationContext context, ContentHash hash)
        {
            var result = await GetFileStreamAsync(context, hash);
            if (!result.Succeeded && CopyRequestHandler != null)
            {
                var copyResult = await CopyRequestHandler.HandleCopyFileRequestAsync(context, hash, context.Token);
                if (copyResult.Succeeded)
                {
                    return await GetFileStreamAsync(context, hash);
                }
            }

            return result;
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

            // ColdStorage is last in the lookup order so no time is wasted during the main GRPC search
            if (_coldStorage != null)
            {
                OpenStreamResult result = await _coldStorage.OpenStreamAsync(context, hash, CancellationToken.None);
                if (result.Code != OpenStreamResult.ResultCode.ContentNotFound)
                {
                    return result;
                }
            }

            return new OpenStreamResult(OpenStreamResult.ResultCode.ContentNotFound, $"{hash.ToShortString()} to found");
        }

        private CopyLimiter CanHandleCopyRequest(bool respectConcurrencyLimit)
        {
            var operationId = Guid.NewGuid();
            var (_, overTheLimit) = _copyFromConcurrencyLimiter.TryAdd(operationId, respectTheLimit: respectConcurrencyLimit);

            return new CopyLimiter(_copyFromConcurrencyLimiter, operationId, overTheLimit);
        }

        private Task SendErrorResponseAsync(ServerCallContext callContext, ResultBase result)
        {
            Contract.Requires(!result.Succeeded);

            if (callContext.CancellationToken.IsCancellationRequested)
            {
                // Nothing we can do: the connection is closed by the caller.
                return BoolResult.SuccessTask;
            }

            string errorType = result.GetType().Name;
            string errorMessage = result.ErrorMessage!;

            if (result.Exception != null)
            {
                errorType = result.Exception.GetType().Name;
                errorMessage = result.Exception.Message;
            }

            return SendErrorResponseAsync(callContext, errorType, errorMessage);
        }

        private Task SendErrorResponseAsync(ServerCallContext context, string errorType, string errorMessage)
        {
            Metadata headers = new Metadata();
            headers.Add("Exception", errorType);
            headers.Add("Message", errorMessage);
            return context.WriteResponseHeadersAsync(headers);
        }

        private Task HandleCopyRequestAsync(CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext callContext)
        {
            var cacheContext = new OperationContext(new Context(request.TraceId, Logger));
            return HandleRequestAsync(
                cacheContext,
                request.GetContentHash(),
                callContext,
                func: async operationContext => await HandleCopyRequestCoreAsync(operationContext, request, responseStream, callContext),
                sendErrorResponseFunc: header => TryWriteAsync(cacheContext, callContext, responseStream, new CopyFileResponse() { Header = header }),
                counter: GrpcContentServerCounters.HandleCopyFile);
        }

        private async Task<BoolResult> HandleCopyRequestCoreAsync(OperationContext operationContext, CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext callContext)
        {
            using var limiter = CanHandleCopyRequest(request.FailFastIfBusy);
            string message;
            if (limiter.OverTheLimit)
            {
                Counters[GrpcContentServerCounters.CopyRequestRejected].Increment();
                message = $"Copy limit of '{limiter.Limit}' reached. Current number of pending copies: {limiter.CurrentCount}";
                await SendErrorResponseAsync(
                    callContext,
                    errorType: "ConcurrencyLimitReached",
                    errorMessage: message);
                return new BoolResult(message);
            }

            ContentHash hash = request.GetContentHash();
            OpenStreamResult result = await GetFileStreamAsync(operationContext, hash);
            switch (result.Code)
            {
                case OpenStreamResult.ResultCode.ContentNotFound:
                    Counters[GrpcContentServerCounters.CopyContentNotFound].Increment();
                    message = $"Requested content with hash={hash.ToShortString()} not found.";
                    await SendErrorResponseAsync(
                        callContext,
                        errorType: "ContentNotFound",
                        errorMessage: message);
                    return new BoolResult(message);
                case OpenStreamResult.ResultCode.Error:
                    Contract.Assert(!result.Succeeded);
                    Counters[GrpcContentServerCounters.CopyError].Increment();
                    await SendErrorResponseAsync(callContext, result);

                    return new BoolResult(result);
                case OpenStreamResult.ResultCode.Success:
                {
                    Contract.Assert(result.Succeeded);
                    using var _ = result.Stream;
                    long size = result.Stream.Length;

                    CopyCompression compression = request.Compression;
                    StreamContentDelegate streamContent;
                    switch (compression)
                    {
                        case CopyCompression.None:
                            streamContent = StreamContentAsync;
                            break;
                        case CopyCompression.Gzip:
                            streamContent = StreamContentWithGzipCompressionAsync;
                            break;
                        default:
                            Tracer.Error(operationContext, $"Requested compression algorithm '{compression}' is unknown by the server. Transfer will be uncompressed.");
                            compression = CopyCompression.None;
                            streamContent = StreamContentAsync;
                            break;
                    }

                    Metadata headers = new Metadata();
                    headers.Add("FileSize", size.ToString());
                    headers.Add("Compression", compression.ToString());
                    headers.Add("ChunkSize", _bufferSize.ToString());

                    // Sending the response headers.
                    await callContext.WriteResponseHeadersAsync(headers);

                    // Cancelling the operation if requested.
                    operationContext.Token.ThrowIfCancellationRequested();

                    using var bufferHandle = _pool.Get();
                    using var secondaryBufferHandle = _pool.Get();

                    // Checking an error potentially injected by tests.
                    if (HandleRequestFailure != null)
                    {
                        streamContent = (input, buffer, secondaryBuffer, stream, ct) => throw HandleRequestFailure;
                    }

                    var streamResult = await operationContext.PerformOperationAsync(
                            Tracer,
                            async () =>
                            {
                                var streamResult = await streamContent(result.Stream, bufferHandle.Value, secondaryBufferHandle.Value, responseStream, operationContext.Token);
                                if (!streamResult.Succeeded && IsKnownGrpcInvalidOperationError(streamResult.ToString()))
                                {
                                    // This is not a critical error. Its just a race condition when the stream is closed by the remote host for 
                                    // some reason and we still streaming the content.
                                    streamResult.MakeNonCritical();
                                }

                                return streamResult;
                            },
                            traceOperationStarted: false, // Tracing only stop messages
                            extraEndMessage: r => $"Hash=[{hash.ToShortString()}] Compression=[{compression}] Size=[{size}] Sender=[{GetSender(callContext)}] ReadTime=[{getFileIoDurationAsString()}]",
                            counter: Counters[GrpcContentServerCounters.CopyStreamContentDuration]);

                    // Cancelling the operation if requested.
                    operationContext.Token.ThrowIfCancellationRequested();

                    streamResult
                        .Then((r, duration) => trackMetrics(r.totalChunksCount, r.totalBytes, duration))
                        .IgnoreFailure(); // The error was already logged.

                    // The caller will catch the error and will send the response to the caller appropriately.
                    streamResult.ThrowIfFailure();

                    return BoolResult.Success;

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
                default:
                    throw new NotImplementedException($"Unknown result.Code '{result.Code}'.");
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
                async context =>
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
        private Task HandlePushFileAsync(IAsyncStreamReader<PushFileRequest> requestStream, IServerStreamWriter<PushFileResponse> responseStream, ServerCallContext callContext)
        {
            var pushRequest = PushRequest.FromMetadata(callContext.RequestHeaders);
            var cacheContext = new OperationContext(new Context(pushRequest.TraceId, Logger));
            var hash = pushRequest.Hash;

            return HandleRequestAsync(
                cacheContext,
                hash,
                callContext,
                operationContext => HandlePushFileCoreAsync(operationContext, pushRequest, requestStream, responseStream, callContext),
                sendErrorResponseFunc: header => TryWriteAsync(cacheContext, callContext, responseStream, new PushFileResponse {Header = header}),
                GrpcContentServerCounters.HandlePushFile);
        }

        private async Task<BoolResult> HandlePushFileCoreAsync(
            OperationContext operationContext,
            PushRequest pushRequest,
            IAsyncStreamReader<PushFileRequest> requestStream,
            IServerStreamWriter<PushFileResponse> responseStream,
            ServerCallContext callContext)
        {
            var startTime = DateTime.UtcNow;
            var hash = pushRequest.Hash;

            var token = operationContext.Token;

            var store = PushFileHandler;

            using var limiter = PushCopyLimiter.Create(operationContext, _ongoingPushesConcurrencyLimiter, hash, store);
            if (limiter.RejectionReason != RejectionReason.Accepted)
            {
                var rejectCounter = limiter.RejectCounter;
                if (rejectCounter != null)
                {
                    Counters[rejectCounter.Value].Increment();
                }

                await callContext.WriteResponseHeadersAsync(PushResponse.DoNotCopy(limiter.RejectionReason).Metadata);
                return new BoolResult($"Copy is skipped. Hash={hash.ToShortString()}, Reason={limiter.RejectionReason}, Limit={limiter.Limit}, CurrentCount={limiter.CurrentCount}.");
            }

            await callContext.WriteResponseHeadersAsync(PushResponse.Copy.Metadata);

            // Checking an error potentially injected by tests.
            if (HandleRequestFailure != null)
            {
                throw HandleRequestFailure;
            }

            using (var disposableFile = new DisposableFile(operationContext, _fileSystem, _temporaryDirectory!.CreateRandomFileName()))
            {
                // NOTE(jubayard): DeleteOnClose not used here because the file needs to be placed into the CAS.
                // Opening a file for read/write and then doing pretty much anything to it leads to weird behavior
                // that needs to be tested on a case by case basis. Since we don't know what the underlying store
                // plans to do with the file, it is more robust to just use the DisposableFile construct.
                using (var tempFile = _fileSystem.OpenForWrite(disposableFile.Path, expectingLength: null, FileMode.CreateNew, FileShare.None))
                {
                    // From the docs: On the server side, MoveNext() does not throw exceptions.
                    // In case of a failure, the request stream will appear to be finished (MoveNext will return false)
                    // and the CancellationToken associated with the call will be cancelled to signal the failure.

                    // It means that if the token is canceled the following method won't throw but will return early.
                    await GrpcExtensions.CopyChunksToStreamAsync(requestStream, tempFile.Stream, request => request.Content, cancellationToken: token);
                }

                token.ThrowIfCancellationRequested();

                Contract.Assert(store != null);
                var result = await store.HandlePushFileAsync(operationContext, hash, new FileSource(disposableFile.Path, FileRealizationMode.Move), token);

                var response = result
                    ? new PushFileResponse { Header = ResponseHeader.Success(startTime) }
                    : new PushFileResponse { Header = ResponseHeader.Failure(startTime, result.ErrorMessage, result.Diagnostics) };

                await responseStream.WriteAsync(response);
                return BoolResult.Success;
            }
        }

        private async Task HandleRequestAsync(
            OperationContext cacheContext,
            ContentHash contentHash,
            ServerCallContext callContext,
            Func<OperationContext, Task<BoolResult>> func,
            Func<ResponseHeader, Task> sendErrorResponseFunc,
            GrpcContentServerCounters counter,
            [CallerMemberName]string caller = null!)
        {
            // Detaching from the calling thread to (potentially) avoid IO Completion port thread exhaustion
            await Task.Yield();
            var startTime = DateTime.UtcNow;

            // Cancelling the operation if the instance is shutting down.
            using var shutdownTracker = TrackShutdown(cacheContext, callContext.CancellationToken);
            var operationContext = shutdownTracker.Context;

            string extraErrorMessage = string.Empty;

            // Using PerformOperation to return the information about any potential errors happen in this code.
            await operationContext
                .PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        try
                        {
                            return await func(operationContext);
                        }
                        catch (Exception e)
                        {
                            // The callback may fail with 'InvalidOperationException' if the other side will close the connection
                            // during the call.
                            if (operationContext.Token.IsCancellationRequested)
                            {
                                if (!callContext.CancellationToken.IsCancellationRequested)
                                {
                                    extraErrorMessage = ", Cancelled by handler";
                                    // The operation is canceled by the handler, not by the caller.
                                    // Sending the response back to notify the caller about the cancellation.

                                    // TODO: consider passing a special error code for cancellation.
                                    await sendErrorResponseFunc(ResponseHeader.Failure(startTime, "Operation cancelled by handler."));
                                }
                                else
                                {
                                    extraErrorMessage = ", Cancelled by caller";
                                }

                                // The operation is cancelled by the caller. Nothing we can do except to trace the error.
                                return new BoolResult(e) {IsCancelled = true};
                            }

                            if (e is InvalidOperationException ioe && IsKnownGrpcInvalidOperationError(ioe.Message))
                            {
                                // in some rare cases its still possible to get 'Already finished' error
                                // even when the tokens are not set.
                                return new BoolResult($"The connection is closed with '{ioe.Message}' message.") { IsCancelled = true };
                            }

                            // Unknown error occurred.
                            // Sending reply back to the caller.
                            string errorDetails = e is ResultPropagationException rpe ? rpe.Result.ToString() : e.ToString();
                            await sendErrorResponseFunc(
                                ResponseHeader.Failure(
                                    startTime,
                                    $"Unknown error occurred processing hash {contentHash.ToShortString()}",
                                    diagnostics: errorDetails));

                            return new BoolResult(e);
                        }
                    },
                    counter: Counters[counter],
                    traceErrorsOnly: true,
                    extraEndMessage: _ => $"Hash=[{contentHash}]{extraErrorMessage}",
                    caller: caller)
                .IgnoreFailure(); // The error was already traced.
        }

        public static bool IsKnownGrpcInvalidOperationError(string? error) => error?.Contains("Already finished") == true || error?.Contains("Shutdown has already been called") == true;

        private async Task TryWriteAsync<TResponse>(OperationContext operationContext, ServerCallContext callContext, IServerStreamWriter<TResponse> writer, TResponse response)
        {
            try
            {
                await writer.WriteAsync(response);
            }
            catch (Exception e)
            {
                if (e is InvalidOperationException && callContext.CancellationToken.IsCancellationRequested)
                {
                    // This is an expected race condition when the connection can be closed when we send the response back.
                    Tracer.Debug(operationContext, "The connection was closed when sending the response back to the caller.");
                }
                else
                {
                    Tracer.Warning(operationContext, e, "Failure sending error response back.");
                }
            }
        }

        private delegate Task<Result<(long totalChunksCount, long totalBytes)>> StreamContentDelegate(Stream input, byte[] buffer, byte[] secondaryBuffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct);

        private async Task<Result<(long totalChunksCount, long totalBytes)>> StreamContentAsync(Stream input, byte[] buffer, byte[] secondaryBuffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct)
        {
            return await GrpcExtensions.CopyStreamToChunksAsync(
                input,
                responseStream,
                (content, chunks) => new CopyFileResponse() { Content = content, Index = chunks},
                buffer,
                secondaryBuffer,
                cancellationToken: ct);
        }

        private async Task<Result<(long totalChunksCount, long totalBytes)>> StreamContentWithGzipCompressionAsync(Stream input, byte[] buffer, byte[] secondaryBuffer, IServerStreamWriter<CopyFileResponse> responseStream, CancellationToken ct)
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
                using (Stream compressionStream = new GZipStream(grpcStream, CompressionLevel.Fastest, true))
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
                    Contract.Assert(session != null);
                    
                    PinResult pinResult = await session.PinAsync(
                        context.OperationContext,
                        request.ContentHash.ToContentHash((HashType)request.HashType),
                        context.Token,
                        urgencyHint: (UrgencyHint)request.Header.UrgencyHint);
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
                    Contract.Assert(session != null);
                    var pinList = new List<ContentHash>();
                    foreach (var hash in request.Hashes)
                    {
                        pinList.Add(hash.ContentHash.ToContentHash((HashType)hash.HashType));
                    }

                    List<Task<Indexed<PinResult>>> pinResults = (await session.PinAsync(
                        context.OperationContext,
                        pinList,
                        context.Token,
                        urgencyHint: (UrgencyHint)request.Header.UrgencyHint)).ToList();
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
                    Contract.Assert(session != null);
                    PlaceFileResult placeFileResult = await session.PlaceFileAsync(
                        context.OperationContext,
                        request.ContentHash.ToContentHash((HashType)request.HashType),
                        new AbsolutePath(request.Path),
                        (FileAccessMode)request.FileAccessMode,
                        FileReplacementMode.ReplaceExisting, // Hard-coded because the service can't tell if this is a retry (where the previous try may have left a partial file)
                        (FileRealizationMode)request.FileRealizationMode,
                        token,
                        urgencyHint: (UrgencyHint)request.Header.UrgencyHint);
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
                    Contract.Assert(session != null);
                    PutResult putResult;
                    if (request.ContentHash == ByteString.Empty)
                    {
                        putResult = await session.PutFileAsync(
                            context.OperationContext,
                            (HashType)request.HashType,
                            new AbsolutePath(request.Path),
                            (FileRealizationMode)request.FileRealizationMode,
                            context.Token,
                            urgencyHint: (UrgencyHint)request.Header.UrgencyHint);
                    }
                    else
                    {
                        putResult = await session.PutFileAsync(
                            context.OperationContext,
                            request.ContentHash.ToContentHash((HashType)request.HashType),
                            new AbsolutePath(request.Path),
                            (FileRealizationMode)request.FileRealizationMode,
                            context.Token,
                            urgencyHint: (UrgencyHint)request.Header.UrgencyHint);
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
                (context, _) => Task.FromResult(new HeartbeatResponse { Header = ResponseHeader.Success(context.StartTime) }),
                (context, errorMessage) => new HeartbeatResponse { Header = ResponseHeader.Failure(context.StartTime, errorMessage) },
                token,
                // It is important to trace heartbeat messages because lack of them will cause sessions to expire.
                traceStartAndStop: true);
        }

        private Task<DeleteContentResponse> DeleteAsync(DeleteContentRequest request, CancellationToken ct)
        {
            return RunFuncNoSessionAsync(
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
            Func<RequestContext, IContentSession?, Task<T>> taskFunc,
            Func<RequestContext, string, T> failFunc,
            CancellationToken token,
            bool? traceStartAndStop = null,
            bool obtainSession = true,
            [CallerMemberName] string operation = null!)
        {
            bool trace = traceStartAndStop ?? TraceGrpcOperations;

            var tracingContext = new Context(header.TraceId, Logger);
            using var shutdownTracker = TrackShutdown(tracingContext, token);

            var context = new RequestContext(startTime: DateTime.UtcNow, shutdownTracker.Context);
            int sessionId = header.SessionId;

            ISessionReference<IContentSession>? sessionOwner = null;
            if (obtainSession && !ContentSessionHandler.TryGetSession(sessionId, out sessionOwner))
            {
                string message = $"Could not find session by Id. {sessionId.AsTraceableSessionId()}";
                Logger.Info(message);
                return failFunc(context, message);
            }

            // if obtainSession is false, then sessionOwner will be null and its ok to pass 'null' to 'using' block.
            using (sessionOwner)
            {
                IContentSession? session = sessionOwner?.Session;

                var sw = StopwatchSlim.Start();

                // Detaching from the calling thread to (potentially) avoid IO Completion port thread exhaustion
                await Task.Yield();

                try
                {
                    TraceGrpcOperationStarted(tracingContext, enabled: trace, operation, sessionId);
                    var result = await taskFunc(context, session);
                    TraceGrpcOperationFinished(tracingContext, enabled: trace, operation, sw.Elapsed, sessionId);

                    return result;
                }
                catch (TaskCanceledException e)
                {
                    var message = GetLogMessage(e, operation, sessionId);
                    Tracer.OperationFinished(tracingContext, FromException(e), sw.Elapsed, message, operation);
                    return failFunc(context, message);
                }
                catch (Exception e)
                {
                    var message = GetLogMessage(e, operation, sessionId);
                    Tracer.OperationFinished(tracingContext, FromException(e), sw.Elapsed, message, operation);
                    return failFunc(context, $"{message}. Error={e}");
                }
            }
        }

        /// <nodoc />
        protected void TraceGrpcOperationStarted(Context tracingContext, bool enabled, string operation, int sessionId)
        {
            if (enabled)
            {
                Tracer.OperationStarted(tracingContext, operation, enabled: true, additionalInfo: sessionId.AsTraceableSessionId());
            }
        }

        /// <nodoc />
        protected void TraceGrpcOperationFinished(Context tracingContext, bool enabled, string operation, TimeSpan duration, int sessionId)
        {
            if (enabled)
            {
                Tracer.OperationFinished(tracingContext, BoolResult.Success, duration, sessionId.AsTraceableSessionId(), operation, traceErrorsOnly: false);
            }
        }

        /// <summary>
        /// Gets the log message for tracing purposes.
        /// </summary>
        protected static string GetLogMessage(Exception e, string operation, int sessionId) => $"The GRPC server operation {operation} {(IsCancelled(e) ? "was cancelled" : "failed")}. {sessionId.AsTraceableSessionId()}";

        /// <nodoc />
        protected static BoolResult FromException(Exception e)
        {
            return new BoolResult(e) {IsCancelled = IsCancelled(e)};
        }

        private static bool IsCancelled(Exception e) => e is TaskCanceledException or OperationCanceledException;

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
            public override Task<ExistenceResponse> CheckFileExists(ExistenceRequest request, ServerCallContext context) => throw new NotSupportedException("The operation 'CheckFileExists' is not supported.");

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
