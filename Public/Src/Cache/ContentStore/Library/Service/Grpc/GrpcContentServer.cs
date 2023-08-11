// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
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
using BuildXL.Utilities;
using BuildXL.Utilities.Core.Tracing;
using ContentStore.Grpc;
using Google.Protobuf;
using Grpc.Core;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using PinRequest = ContentStore.Grpc.PinRequest;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc;

/// <summary>
/// A CAS server implementation based on gRPC.
/// </summary>
public class GrpcContentServer : GrpcCopyServer, IDistributedStreamStore
{
    /// <nodoc />
    public new record Configuration : GrpcCopyServer.Configuration
    {
        public bool TraceGrpcOperations { get; set; } = false;

        public int ProactivePushCountLimit { get; set; } = LocalServerConfiguration.DefaultProactivePushCountLimit;

        public AbsolutePath? WorkingDirectory { get; set; }

        public override void From(LocalServerConfiguration configuration)
        {
            base.From(configuration);

            TraceGrpcOperations = configuration.TraceGrpcOperations;
            ConfigurationHelper.ApplyIfNotNull(configuration.ProactivePushCountLimit, v => ProactivePushCountLimit = v);
            WorkingDirectory = configuration.DataRootPath / "GrpcContentServer" / "temp";
        }
    }

    /// <nodoc />
    public IPushFileHandler? PushFileHandler { get; }

    /// <nodoc />
    public ICopyRequestHandler? CopyRequestHandler { get; }

    /// <nodoc/>
    public IDistributedStreamStore StreamStore => this;

    /// <summary>
    /// Session handler for <see cref="IContentSession"/>
    /// </summary>
    /// <remarks>
    /// This is a hack to allow for an <see cref="ISessionHandler{TSession, TSessionData}"/> with other sessions that inherit from
    /// <see cref="IContentSession"/> with session data which inherits from <see cref="LocalContentServerSessionData"/> to be used instead.
    /// </remarks>
    private readonly ISessionHandler<IContentSession, LocalContentServerSessionData> _contentSessionHandler;
    private readonly Capabilities _serviceCapabilities;
    private readonly IAbsFileSystem _fileSystem;
    private readonly DisposableDirectory _temporaryDirectory;
    private readonly Configuration _configuration;
    private readonly ConcurrencyLimiter<ContentHash> _ongoingPushesConcurrencyLimiter;

    /// <nodoc />
    public GrpcContentServer(
        ILogger logger,
        Capabilities serviceCapabilities,
        ISessionHandler<IContentSession, LocalContentServerSessionData> sessionHandler,
        IReadOnlyDictionary<string, IContentStore> storesByName,
        Configuration configuration,
        IAbsFileSystem? fileSystem = null)
        : base(logger, storesByName, configuration)
    {
        _configuration = configuration;
        _serviceCapabilities = serviceCapabilities;
        _ongoingPushesConcurrencyLimiter = new ConcurrencyLimiter<ContentHash>(configuration.ProactivePushCountLimit);
        _contentSessionHandler = sessionHandler;

        _fileSystem = fileSystem ?? new PassThroughFileSystem(logger);

        _temporaryDirectory = new DisposableDirectory(_fileSystem, _configuration.WorkingDirectory);
        PushFileHandler = storesByName.Values.OfType<IPushFileHandler>().FirstOrDefault();
        CopyRequestHandler = ContentStoreByCacheName.Values.OfType<ICopyRequestHandler>().FirstOrDefault();

        GrpcAdapter = new ContentServerAdapter(this);
    }

    /// <inheritdoc />
    protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
    {
        _temporaryDirectory.Dispose();
        return BoolResult.SuccessTask;
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

        var sessionCreationResult = await _contentSessionHandler.CreateSessionAsync(
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
        await _contentSessionHandler.ReleaseSessionAsync(new OperationContext(cacheContext, token), request.Header.SessionId);
        return new ShutdownResponse();
    }

    /// <nodoc />
#pragma warning disable IDE0060 // Remove unused parameter
    public Task<HelloResponse> HelloAsync(HelloRequest request, CancellationToken token)
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
        var counters = await _contentSessionHandler.GetStatsAsync(new OperationContext(cacheContext, token));
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
    public async Task<RemoveFromTrackerResponse> RemoveFromTrackerAsync(
        RemoveFromTrackerRequest request,
        CancellationToken token)
    {
        DateTime startTime = DateTime.UtcNow;
        var cacheContext = new Context(request.TraceId, Logger);
        using var shutdownTracker = TrackShutdown(cacheContext, token);

        var removeFromTrackerResult = await _contentSessionHandler.RemoveFromTrackerAsync(shutdownTracker.Context);
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

    /// <summary>
    /// Implements a request copy file request
    /// </summary>
    public Task<RequestCopyFileResponse> RequestCopyFileAsync(RequestCopyFileRequest request, CancellationToken cancellationToken)
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

                if (ContentStoreByCacheName.Values.OfType<ICopyRequestHandler>().FirstOrDefault() is ICopyRequestHandler handler)
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
    public Task HandlePushFileAsync(IAsyncStreamReader<PushFileRequest> requestStream, IServerStreamWriter<PushFileResponse> responseStream, ServerCallContext callContext)
    {
        var pushRequest = PushRequest.FromMetadata(callContext.RequestHeaders);
        var cacheContext = new OperationContext(new Context(pushRequest.TraceId, Logger));
        var hash = pushRequest.Hash;

        return HandleRequestAsync(
            cacheContext,
            hash,
            callContext,
            operationContext => HandlePushFileCoreAsync(operationContext, pushRequest, requestStream, responseStream, callContext),
            sendErrorResponseFunc: header => TryWriteAsync(cacheContext, callContext, responseStream, new PushFileResponse { Header = header }),
            GrpcServerCounters.HandlePushFile);
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

        using var disposableFile = _temporaryDirectory.CreateTemporaryFile(operationContext);

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

    /// <summary>
    /// Implements a pin request.
    /// </summary>
    public Task<PinResponse> PinAsync(PinRequest request, CancellationToken token)
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
    public Task<PinBulkResponse> PinBulkAsync(PinBulkRequest request, CancellationToken token)
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
    public Task<PlaceFileResponse> PlaceFileAsync(PlaceFileRequest request, CancellationToken token)
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
    public Task<PutFileResponse> PutFileAsync(PutFileRequest request, CancellationToken token)
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
                    HashType = (int)putResult.ContentHash.HashType,
                    AlreadyInCache = putResult.ContentAlreadyExistsInCache,
                };
            },
            (context, errorMessage) => new PutFileResponse { Header = ResponseHeader.Failure(context.StartTime, errorMessage) },
            token: token);
    }

    /// <summary>
    /// Implements a heartbeat request for a session.
    /// </summary>
    public Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken token)
    {
        return RunFuncAsync(
            request.Header,
            (context, _) => Task.FromResult(new HeartbeatResponse { Header = ResponseHeader.Success(context.StartTime) }),
            (context, errorMessage) => new HeartbeatResponse { Header = ResponseHeader.Failure(context.StartTime, errorMessage) },
            token,
            // It is important to trace heartbeat messages because lack of them will cause sessions to expire.
            traceStartAndStop: true);
    }

    public Task<DeleteContentResponse> DeleteAsync(DeleteContentRequest request, CancellationToken ct)
    {
        return RunFuncNoSessionAsync(
            request.TraceId,
            async context =>
            {
                var contentHash = request.ContentHash.ToContentHash((HashType)request.HashType);

                var deleteOptions = new DeleteContentOptions() { DeleteLocalOnly = request.DeleteLocalOnly };
                var deleteResults = await Task.WhenAll<DeleteResult>(ContentStoreByCacheName.Values.Select(store => store.DeleteAsync(context.OperationContext, contentHash, deleteOptions)));

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
        bool trace = traceStartAndStop ?? _configuration.TraceGrpcOperations;

        var tracingContext = new Context(header.TraceId, Logger);
        using var shutdownTracker = TrackShutdown(tracingContext, token);
        var context = new RequestContext(startTime: DateTime.UtcNow, shutdownTracker.Context);

        if (shutdownTracker.Context.Token.IsCancellationRequested)
        {
            string message = $"Could not finish the operation '{operation}' because the shutdown was initiated.";
            Logger.Info(message);
            return failFunc(context, message);
        }

        int sessionId = header.SessionId;

        ISessionReference<IContentSession>? sessionOwner = null;
        if (obtainSession && !_contentSessionHandler.TryGetSession(sessionId, out sessionOwner))
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
        return new BoolResult(e) { IsCancelled = IsCancelled(e) };
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

        public GrpcServerCounters? RejectCounter =>
            RejectionReason switch
            {
                RejectionReason.NotSupported => GrpcServerCounters.PushFileRejectNotSupported,
                RejectionReason.CopyLimitReached => GrpcServerCounters.PushFileRejectCopyLimitReached,
                RejectionReason.OngoingCopy => GrpcServerCounters.PushFileRejectCopyOngoingCopy,
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

}
