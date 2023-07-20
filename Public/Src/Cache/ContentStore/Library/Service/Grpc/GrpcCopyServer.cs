// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using ContentStore.Grpc;
using Google.Protobuf;
using Grpc.Core;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace BuildXL.Cache.ContentStore.Service.Grpc;

#nullable enable

public class GrpcCopyServer : StartupShutdownSlimBase
{
    public record Configuration
    {
        public int CopyBufferSize { get; set; } = ContentStore.Grpc.GrpcConstants.DefaultBufferSizeBytes;

        public int CopyRequestHandlingCountLimit { get; set; } = LocalServerConfiguration.DefaultCopyRequestHandlingCountLimit;

        public virtual void From(LocalServerConfiguration configuration)
        {
            ConfigurationHelper.ApplyIfNotNull(configuration.BufferSizeForGrpcCopies, v => CopyBufferSize = v);
            ConfigurationHelper.ApplyIfNotNull(configuration.CopyRequestHandlingCountLimit, v => CopyRequestHandlingCountLimit = v);
        }
    }

    protected override Tracer Tracer { get; }
    
    /// <nodoc />
    protected readonly ILogger Logger;

    /// <nodoc />
    public CounterCollection<GrpcServerCounters> Counters { get; } = new();

    /// <summary>
    /// This adapter routes messages from gRPC to the current class.
    /// </summary>
    /// <remarks>
    /// Expected to be read-only after construction. Child classes may overwrite the field in their constructor,
    /// but not afterwards, or behavior will be undefined.
    /// </remarks>
    public IGrpcServiceEndpoint GrpcAdapter { get; protected set; }

    /// <summary>
    /// [Used for testing only]: an exception that will be thrown during copy files or handling push files.
    /// </summary>
    internal Exception? HandleRequestFailure { get; set; }

    protected readonly IReadOnlyDictionary<string, IContentStore> ContentStoreByCacheName;

    private readonly Configuration _configuration;
    private readonly ByteArrayPool _copyBufferPool;

    private readonly ConcurrencyLimiter<Guid> _copyFromConcurrencyLimiter;

    /// <nodoc />
    public GrpcCopyServer(
        ILogger logger,
        IReadOnlyDictionary<string, IContentStore> storesByName,
        Configuration configuration)
    {
        Tracer = new Tracer(GetType().Name);
        Logger = logger;
        ContentStoreByCacheName = storesByName;
        _configuration = configuration;

        _copyFromConcurrencyLimiter = new ConcurrencyLimiter<Guid>(_configuration.CopyRequestHandlingCountLimit);
        _copyBufferPool = new ByteArrayPool(_configuration.CopyBufferSize);

        GrpcAdapter = new CopyServerAdapter(this);
    }


    public Task HandleCopyRequestAsync(CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext callContext)
    {
        var cacheContext = new OperationContext(new Context(request.TraceId, Logger));
        return HandleRequestAsync(
            cacheContext,
            request.GetContentHash(),
            callContext,
            func: async operationContext => await HandleCopyRequestCoreAsync(operationContext, request, responseStream, callContext),
            sendErrorResponseFunc: header => TryWriteAsync(cacheContext, callContext, responseStream, new CopyFileResponse() { Header = header }),
            counter: GrpcServerCounters.HandleCopyFile);
    }

    private async Task<BoolResult> HandleCopyRequestCoreAsync(OperationContext operationContext, CopyFileRequest request, IServerStreamWriter<CopyFileResponse> responseStream, ServerCallContext callContext)
    {
        using var limiter = CanHandleCopyRequest(request.FailFastIfBusy);
        string message;
        if (limiter.OverTheLimit)
        {
            Counters[GrpcServerCounters.CopyRequestRejected].Increment();
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
                Counters[GrpcServerCounters.CopyContentNotFound].Increment();
                message = $"Requested content with hash={hash.ToShortString()} not found.";
                await SendErrorResponseAsync(
                    callContext,
                    errorType: "ContentNotFound",
                    errorMessage: message);
                return new BoolResult(message);
            case OpenStreamResult.ResultCode.Error:
                Contract.Assert(!result.Succeeded);
                Counters[GrpcServerCounters.CopyError].Increment();
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

                var headers = new Metadata
                              {
                                  { "FileSize", size.ToString() },
                                  { "Compression", compression.ToString() },
                                  { "ChunkSize", _configuration.CopyBufferSize.ToString() }
                              };

                // Sending the response headers.
                await callContext.WriteResponseHeadersAsync(headers);

                // Cancelling the operation if requested.
                operationContext.Token.ThrowIfCancellationRequested();

                using var bufferHandle = _copyBufferPool.Get();
                using var secondaryBufferHandle = _copyBufferPool.Get();

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
                    counter: Counters[GrpcServerCounters.CopyStreamContentDuration]);

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

                    ConfigurationHelper.ApplyIfNotNull(GetFileIODuration(result.Stream), d => Tracer.TrackMetric(operationContext, "HandleCopyFile_FileIoDurationMs", (long)d.TotalMilliseconds));
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

    protected async Task HandleRequestAsync(
        OperationContext cacheContext,
        ContentHash contentHash,
        ServerCallContext callContext,
        Func<OperationContext, Task<BoolResult>> func,
        Func<ResponseHeader, Task> sendErrorResponseFunc,
        GrpcServerCounters counter,
        [CallerMemberName] string caller = null!)
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
                            return new BoolResult(e) { IsCancelled = true };
                        }

                        if (e is InvalidOperationException ioe && IsKnownGrpcInvalidOperationError(ioe.Message))
                        {
                            // in some rare cases its still possible to get 'Already finished' error
                            // even when the tokens are not set.
                            return new BoolResult($"The connection is closed with '{ioe.Message}' message.") { IsCancelled = true };
                        }

                        // Unknown error occurred.
                        // Sending reply back to the caller.
                        string errorDetails = e is ResultPropagationException rpe ? rpe.Result.ToString() : e.ToStringDemystified();
                        await sendErrorResponseFunc(
                            ResponseHeader.Failure(
                                startTime,
                                $"Unknown error occurred processing hash {contentHash}",
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

    protected async Task TryWriteAsync<TResponse>(OperationContext operationContext, ServerCallContext callContext, IServerStreamWriter<TResponse> writer, TResponse response)
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
            (content, chunks) => new CopyFileResponse() { Content = content, Index = chunks },
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
            Counters.AddToCounter(GrpcServerCounters.StreamContentReadFromDiskDuration, tfs.ReadDuration);
            return tfs.ReadDuration;
        }

        return null;
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

    protected async Task<OpenStreamResult> GetFileStreamAsync(Context context, ContentHash hash)
    {
        // Iterate through all known stores, looking for content in each.
        // In most of our configurations there is just one store anyway,
        // and doing this means both we can callers don't have
        // to deal with cache roots and drive letters.

        foreach (KeyValuePair<string, IContentStore> entry in ContentStoreByCacheName)
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

    private Task SendErrorResponseAsync(ServerCallContext context, string errorType, string errorMessage)
    {
        Metadata headers = new Metadata();
        headers.Add("Exception", errorType);
        headers.Add("Message", errorMessage);
        return context.WriteResponseHeadersAsync(headers);
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
}
