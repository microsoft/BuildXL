// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStore.Grpc;
using Google.Protobuf;
using Grpc.Core;

// Can't rename ProtoBuf

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// An implementation of a CAS service client based on GRPC.
    /// </summary>
    public class GrpcContentClient : GrpcClientBase, IRpcClient
    {
        /// <nodoc />
        protected readonly ContentServer.ContentServerClient Client;

        private readonly IClock _clock = SystemClock.Instance;

        /// <summary>
        /// Size of the batch used in bulk operations.
        /// </summary>
        protected const int BatchSize = 500;

        /// <nodoc />
        public GrpcContentClient(
            ServiceClientContentSessionTracer tracer,
            IAbsFileSystem fileSystem,
            ServiceClientRpcConfiguration configuration,
            string? scenario)
            : this(tracer, fileSystem, configuration, scenario, Capabilities.ContentOnly)
        {
        }

        /// <nodoc />
        protected GrpcContentClient(
            ServiceClientContentSessionTracer tracer,
            IAbsFileSystem fileSystem,
            ServiceClientRpcConfiguration configuration,
            string? scenario,
            Capabilities capabilities = Capabilities.ContentOnly)
            : base(fileSystem, tracer, configuration, scenario, capabilities)
        {
            Client = new ContentServer.ContentServerClient(Channel);
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> OpenStreamAsync(OperationContext operationContext, ContentHash contentHash)
        {
            var sessionContext = await CreateSessionContextAsync(operationContext);
            if (!sessionContext)
            {
                return new OpenStreamResult(sessionContext);
            }

            AbsolutePath tempPath = sessionContext.Value.SessionData.TemporaryDirectory.CreateRandomFileName();

            var placeFileResult = await PlaceFileAsync(
                operationContext,
                sessionContext.Value,
                contentHash,
                tempPath,
                FileAccessMode.ReadOnly,
                FileReplacementMode.None,
                FileRealizationMode.HardLink);

            return OpenStream(operationContext, tempPath, placeFileResult);
        }

        private OpenStreamResult OpenStream(OperationContext context, AbsolutePath tempPath, PlaceFileResult placeFileResult)
        {
            if (placeFileResult)
            {
                try
                {
                    StreamWithLength? stream = FileSystem.TryOpenReadOnly(tempPath, FileShare.Delete | FileShare.Read);
                    if (stream == null)
                    {
                        throw new ClientCanRetryException(context, $"Failed to open temp file {tempPath}. The service may have restarted");
                    }

                    return new OpenStreamResult(stream);
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
                {
                    throw new ClientCanRetryException(context, $"Failed to open temp file {tempPath}. The service may be restarting", ex);
                }
                catch (Exception ex) when (!(ex is ClientCanRetryException))
                {
                    // The caller's retry policy needs to see ClientCanRetryExceptions in order to properly retry
                    return new OpenStreamResult(ex);
                }
            }
            else if (placeFileResult.Code == PlaceFileResult.ResultCode.NotPlacedContentNotFound)
            {
                return new OpenStreamResult(OpenStreamResult.ResultCode.ContentNotFound, placeFileResult.ErrorMessage);
            }
            else
            {
                return new OpenStreamResult(placeFileResult);
            }
        }

        /// <inheritdoc />
        public Task<PinResult> PinAsync(OperationContext context, ContentHash contentHash)
        {
            return PerformOperationAsync(
                new OperationContext(context),
                sessionContext => Client.PinAsync(
                    new PinRequest
                    {
                        HashType = (int)contentHash.HashType,
                        ContentHash = contentHash.ToByteString(),
                        Header = sessionContext.CreateHeader(),
                    }),
                response => UnpackPinResult(response.Header, response.Info)
                );
        }

        /// <inheritdoc />
        public async Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes)
        {
            if (contentHashes.Count == 0)
            {
                return new List<Task<Indexed<PinResult>>>(0);
            }

            var pinResults = new List<Task<Indexed<PinResult>>>();

            var pinTasks = new List<Task<IEnumerable<Task<Indexed<PinResult>>>>>();
            int i = 0;
            foreach (var chunk in contentHashes.GetPages(BatchSize))
            {
                pinTasks.Add(PinBatchAsync(context, i, chunk.ToList()));
                i += BatchSize;
            }

            pinResults.AddRange((await Task.WhenAll(pinTasks)).SelectMany(pins => pins));
            return pinResults;
        }

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> PinBatchAsync(Context context, int baseIndex, IReadOnlyList<ContentHash> chunk)
        {
            // This operation is quite different from others because there is no single header response.
            // So instead of using a common functionality we have handle this case separately.
            var sessionContext = await CreateSessionContextAsync(context);
            if (!sessionContext)
            {
                PinResult pinResult = new PinResult(sessionContext);
                return chunk.Select((ContentHash h) => pinResult).AsIndexed().AsTasks();
            }

            int sessionId = sessionContext.Value.SessionId;

            var pinResults = new List<Indexed<PinResult>>();
            var bulkPinRequest = new PinBulkRequest { Header = new RequestHeader(context.TraceId, sessionId) };
            foreach (var contentHash in chunk)
            {
                bulkPinRequest.Hashes.Add(
                    new ContentHashAndHashTypeData { HashType = (int)contentHash.HashType, ContentHash = contentHash.ToByteString() });
            }

            PinBulkResponse underlyingBulkPinResponse = await SendGrpcRequestAndThrowIfFailedAsync(
                sessionContext.Value,
                async () => await Client.PinBulkAsync(bulkPinRequest),
                throwFailures: false);

            var info = underlyingBulkPinResponse.Info.Count == 0 ? null : underlyingBulkPinResponse.Info;
            foreach (var response in underlyingBulkPinResponse.Header)
            {
                await ResetOnUnknownSessionAsync(context, response.Value, sessionId);
                pinResults.Add(UnpackPinResult(response.Value, info?[response.Key]).WithIndex(response.Key + baseIndex));
            }

            ServiceClientTracer.LogPinResults(context, pinResults.Select(r => chunk[r.Index - baseIndex]).ToList(), pinResults.Select(r => r.Item).ToList());

            return pinResults.AsTasks();
        }

        private PinResult UnpackPinResult(ResponseHeader header, PinResponseInfo? info)
        {
            // Workaround: Handle the service returning negative result codes in error cases
            var resultCode = header.Result < 0 ? PinResult.ResultCode.Error : (PinResult.ResultCode)header.Result;
            string errorMessage = header.ErrorMessage;
            var result = string.IsNullOrEmpty(errorMessage)
                ? new PinResult(resultCode) { ContentSize = info?.ContentSize ?? -1 }
                : new PinResult(resultCode, errorMessage, header.Diagnostics);

            return result;
        }

        /// <inheritdoc />
        public async Task<PlaceFileResult> PlaceFileAsync(
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode)
        {
            var sessionContext = await CreateSessionContextAsync(context);
            if (!sessionContext)
            {
                return new PlaceFileResult(sessionContext);
            }

            return await PlaceFileAsync(context, sessionContext.Value, contentHash, path, accessMode, replacementMode, realizationMode);
        }

        private Task<PlaceFileResult> PlaceFileAsync(
            OperationContext operationContext,
            SessionContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode)
        {
            return PerformOperationAsync(
                context,
                _ => Client.PlaceFileAsync(
                    new PlaceFileRequest
                    {
                        Header = context.CreateHeader(),
                        HashType = (int)contentHash.HashType,
                        ContentHash = contentHash.ToByteString(),
                        Path = path.Path,
                        FileAccessMode = (int)accessMode,
                        FileRealizationMode = (int)realizationMode,
                        FileReplacementMode = (int)replacementMode
                    },
                    options: GetCallOptions(Configuration.PlaceDeadline, operationContext.Token)),
                response =>
                {
                    // Workaround: Handle the service returning negative result codes in error cases
                    PlaceFileResult.ResultCode resultCode = response.Header.Result < 0
                        ? PlaceFileResult.ResultCode.Error
                        : (PlaceFileResult.ResultCode)response.Header.Result;
                    if (!response.Header.Succeeded)
                    {
                        var message = string.IsNullOrEmpty(response.Header.ErrorMessage)
                            ? resultCode.ToString()
                            : response.Header.ErrorMessage;
                        return new PlaceFileResult(resultCode, message, response.Header.Diagnostics);
                    }
                    else
                    {
                        return PlaceFileResult.CreateSuccess(resultCode, response.ContentSize, (PlaceFileResult.Source)response.MaterializationSource, new DateTime(response.LastAccessTime));
                    }
                });
        }

        private CallOptions GetCallOptions(TimeSpan? operationOverrideDeadline, CancellationToken token)
        {
            return new CallOptions(headers: null, deadline: GetDeadline(operationOverrideDeadline), cancellationToken: token);
        }
        
        private DateTime? GetDeadline(TimeSpan? operationOverrideDeadline)
        {
            var timeout = operationOverrideDeadline ?? Configuration.Deadline;
            if (timeout != null)
            {
                return _clock.UtcNow.Add(timeout.Value);
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<PutResult> PutFileAsync(
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode)
        {
            var sessionContext = await CreateSessionContextAsync(context);

            if (!sessionContext)
            {
                return new PutResult(sessionContext, contentHash);
            }

            return await PutFileAsync(context, sessionContext.Value, contentHash, path, realizationMode);
        }

        private Task<PutResult> PutFileAsync(
            OperationContext operationContext,
            SessionContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode)
        {
            return PerformOperationAsync(
                context,
                sessionContext => Client.PutFileAsync(
                    new PutFileRequest
                    {
                        Header = sessionContext.CreateHeader(),
                        ContentHash = contentHash.ToByteString(),
                        HashType = (int)contentHash.HashType,
                        FileRealizationMode = (int)realizationMode,
                        Path = path.Path
                    },
                    options: this.GetCallOptions(Configuration.Deadline, operationContext.Token)),
                response =>
                {
                    if (!response.Header.Succeeded)
                    {
                        return new PutResult(contentHash, response.Header.ErrorMessage, response.Header.Diagnostics);
                    }
                    else
                    {
                        return new PutResult(response.ContentHash.ToContentHash((HashType)response.HashType), response.ContentSize);
                    }
                });
        }

        /// <inheritdoc />
        public async Task<PutResult> PutFileAsync(
            OperationContext context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode)
        {
            var sessionContext = await CreateSessionContextAsync(context);
            if (!sessionContext)
            {
                return new PutResult(sessionContext, new ContentHash(hashType));
            }

            return await PutFileAsync(context, sessionContext.Value, hashType, path, realizationMode);
        }

        private Task<PutResult> PutFileAsync(
            OperationContext operationContext,
            SessionContext context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode)
        {
            return PerformOperationAsync(
                context,
                sessionContext => Client.PutFileAsync(
                    new PutFileRequest
                    {
                        Header = sessionContext.CreateHeader(),
                        ContentHash = ByteString.Empty,
                        HashType = (int)hashType,
                        FileRealizationMode = (int)realizationMode,
                        Path = path.Path
                    },
                    options: GetCallOptions(Configuration.Deadline, operationContext.Token)),
                response =>
                {
                    if (!response.Header.Succeeded)
                    {
                        return new PutResult(new ContentHash(hashType), response.Header.ErrorMessage, response.Header.Diagnostics);
                    }
                    else
                    {
                        return new PutResult(response.ContentHash.ToContentHash((HashType)response.HashType), response.ContentSize);
                    }
                });
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(OperationContext context, ContentHash contentHash, Stream stream, bool createDirectory)
        {
            return PutStreamInternalAsync(
                context,
                stream,
                contentHash,
                createDirectory: createDirectory,
                (sessionContext, tempFile) => PutFileAsync(context, sessionContext, contentHash, tempFile, FileRealizationMode.HardLink));
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(OperationContext context, HashType hashType, Stream stream, bool createDirectory)
        {
            return PutStreamInternalAsync(
                context,
                stream,
                new ContentHash(hashType),
                createDirectory: createDirectory,
                (sessionContext, tempFile) => PutFileAsync(context, sessionContext, hashType, tempFile, FileRealizationMode.HardLink));
        }

        /// <inheritdoc />
        public async Task<DeleteResult> DeleteContentAsync(OperationContext context, ContentHash hash, bool deleteLocalOnly)
        {
            try
            {
                DeleteContentRequest request = new DeleteContentRequest()
                {
                    TraceId = context.TracingContext.TraceId.ToString(),
                    HashType = (int)hash.HashType,
                    ContentHash = hash.ToByteString(),
                    DeleteLocalOnly = deleteLocalOnly
                };

                DeleteContentResponse response = await Client.DeleteAsync(request, options: GetCallOptions(Configuration.Deadline, context.Token));
                if (!deleteLocalOnly)
                {
                    var deleteResultsMapping = new Dictionary<string, DeleteResult>();
                    foreach (var kvp in response.DeleteResults)
                    {
                        var header = kvp.Value;
                        var deleteResult = string.IsNullOrEmpty(header.ErrorMessage)
                            ? new DeleteResult(
                                (DeleteResult.ResultCode)header.Result,
                                hash,
                                response.ContentSize)
                            : new DeleteResult((DeleteResult.ResultCode)header.Result, header.ErrorMessage, header.Diagnostics);

                        deleteResultsMapping.Add(kvp.Key, deleteResult);
                    }

                    return new DistributedDeleteResult(hash, response.ContentSize, deleteResultsMapping);
                }

                if (response.Header.Succeeded)
                {
                    return new DeleteResult((DeleteResult.ResultCode)response.Result, hash, response.ContentSize);
                }
                else
                {
                    return new DeleteResult((DeleteResult.ResultCode)response.Result, response.Header.ErrorMessage, response.Header.Diagnostics);
                }
            }
            catch (RpcException r)
            {
                if (r.StatusCode == StatusCode.Unavailable)
                {
                    return new DeleteResult(DeleteResult.ResultCode.ServerError, r);
                }
                else
                {
                    return new DeleteResult(DeleteResult.ResultCode.Error, r);
                }
            }
        }

        private async Task<PutResult> PutStreamInternalAsync(OperationContext context, Stream stream, ContentHash contentHash, bool createDirectory, Func<SessionContext, AbsolutePath, Task<PutResult>> putFileFunc)
        {
            var sessionContextResult = await CreateSessionContextAsync(context);
            if (!sessionContextResult)
            {
                return new PutResult(sessionContextResult, contentHash);
            }

            var sessionData = sessionContextResult.Value.SessionData;
            var tempFile = sessionData.TemporaryDirectory.CreateRandomFileName();
            try
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                if (createDirectory)
                {
                    var parentDirectory = tempFile.GetParent();
                    if (!FileSystem.DirectoryExists(parentDirectory))
                    {
                        FileSystem.CreateDirectory(parentDirectory);
                    }
                }

                using (var fileStream = FileSystem.TryOpenForWrite(tempFile, stream.TryGetStreamLength(), FileMode.Create, FileShare.Delete))
                {
                    if (fileStream == null)
                    {
                        throw new ClientCanRetryException(context, $"Could not create temp file {tempFile}. The service may have restarted.");
                    }

                    await stream.CopyToAsync(fileStream);
                }

                PutResult putResult = await putFileFunc(sessionContextResult.Value, tempFile);

                if (putResult.Succeeded)
                {
                    return new PutResult(putResult.ContentHash, putResult.ContentSize);
                }
                else if (!FileSystem.FileExists(tempFile))
                {
                    throw new ClientCanRetryException(context, $"Temp file {tempFile} not found. The service may have restarted.");
                }
                else
                {
                    return new PutResult(putResult, putResult.ContentHash);
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
            {
                throw new ClientCanRetryException(context, "Exception thrown during PutStreamInternal. The service may have shut down", ex);
            }
            catch (Exception ex) when (!(ex is ClientCanRetryException))
            {
                // The caller's retry policy needs to see ClientCanRetryExceptions in order to properly retry
                return new PutResult(ex, contentHash);
            }
        }

        /// <inheritdoc />
        protected override AsyncUnaryCall<ShutdownResponse> ShutdownSessionAsync(ShutdownRequest shutdownRequest)
        {
            return Client.ShutdownSessionAsync(shutdownRequest);
        }

        /// <inheritdoc />
        protected override AsyncUnaryCall<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest heartbeatRequest, CancellationToken token)
        {
            return Client.HeartbeatAsync(heartbeatRequest, cancellationToken: token);
        }

        /// <inheritdoc />
        protected override AsyncUnaryCall<HelloResponse> HelloAsync(HelloRequest helloRequest, CancellationToken token)
        {
            return Client.HelloAsync(helloRequest, cancellationToken: token);
        }

        /// <inheritdoc />
        protected override AsyncUnaryCall<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest createSessionRequest)
        {
            return Client.CreateSessionAsync(createSessionRequest);
        }

        /// <inheritdoc />
        protected override AsyncUnaryCall<GetStatsResponse> GetStatsAsync(GetStatsRequest getStatsRequest)
        {
            return Client.GetStatsAsync(getStatsRequest);
        }
    }
}
