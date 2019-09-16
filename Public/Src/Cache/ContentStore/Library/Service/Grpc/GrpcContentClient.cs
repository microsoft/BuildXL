// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
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

        /// <summary>
        /// Size of the batch used in bulk operations.
        /// </summary>
        protected const int BatchSize = 500;

        /// <nodoc />
        public GrpcContentClient(
            ServiceClientContentSessionTracer tracer,
            IAbsFileSystem fileSystem,
            int grpcPort,
            string scenario,
            TimeSpan? heartbeatInterval = null,
            Capabilities capabilities = Capabilities.ContentOnly)
            : base(fileSystem, tracer, grpcPort, scenario, capabilities, heartbeatInterval)
        {
            GrpcEnvironment.InitializeIfNeeded();
            Client = new ContentServer.ContentServerClient(Channel);
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash)
        {
            var operationContext = new OperationContext(context);
            var sessionContext = await CreateSessionContextAsync(operationContext);
            if (!sessionContext)
            {
                return new OpenStreamResult(sessionContext);
            }

            AbsolutePath tempPath = sessionContext.Value.SessionData.TemporaryDirectory.CreateRandomFileName();

            var placeFileResult = await PlaceFileAsync(
                sessionContext.Value,
                contentHash,
                tempPath,
                FileAccessMode.ReadOnly,
                FileReplacementMode.None,
                FileRealizationMode.HardLink);

            return await OpenStreamAsync(operationContext, tempPath, placeFileResult);
            ;
        }

        private async Task<OpenStreamResult> OpenStreamAsync(OperationContext context, AbsolutePath tempPath, PlaceFileResult placeFileResult)
        {
            if (placeFileResult)
            {
                try
                {
                    Stream stream = await FileSystem.OpenReadOnlyAsync(tempPath, FileShare.Delete | FileShare.Read);
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
        public Task<PinResult> PinAsync(Context context, ContentHash contentHash)
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
                response => UnpackPinResult(response.Header)
                );
        }

        /// <inheritdoc />
        public async Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
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
            var bulkPinRequest = new PinBulkRequest {Header = new RequestHeader(context.Id, sessionId)};
            foreach (var contentHash in chunk)
            {
                bulkPinRequest.Hashes.Add(
                    new ContentHashAndHashTypeData { HashType = (int)contentHash.HashType, ContentHash = contentHash.ToByteString() });
            }

            PinBulkResponse underlyingBulkPinResponse = await SendGrpcRequestAndThrowIfFailedAsync(
                sessionContext.Value,
                async () => await Client.PinBulkAsync(bulkPinRequest),
                throwFailures: false);

            foreach (var response in underlyingBulkPinResponse.Header)
            {
                await ResetOnUnknownSessionAsync(context, response.Value, sessionId);
                pinResults.Add(UnpackPinResult(response.Value).WithIndex(response.Key + baseIndex));
            }

            ServiceClientTracer.LogPinResults(context, pinResults.Select(r => chunk[r.Index - baseIndex]).ToList(), pinResults.Select(r => r.Item).ToList());

            return pinResults.AsTasks();
        }

        private PinResult UnpackPinResult(ResponseHeader header)
        {
            // Workaround: Handle the service returning negative result codes in error cases
            var resultCode = header.Result < 0 ? PinResult.ResultCode.Error : (PinResult.ResultCode)header.Result;
            string errorMessage = header.ErrorMessage;
            return string.IsNullOrEmpty(errorMessage)
                ? new PinResult(resultCode)
                : new PinResult(resultCode, errorMessage, header.Diagnostics);
        }

        /// <inheritdoc />
        public async Task<PlaceFileResult> PlaceFileAsync(
            Context context,
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

            return await PlaceFileAsync(sessionContext.Value, contentHash, path, accessMode, replacementMode, realizationMode);
        }

        private Task<PlaceFileResult> PlaceFileAsync(
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
                    }),
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
                        return new PlaceFileResult(resultCode, response.ContentSize);
                    }
                });
        }

        /// <inheritdoc />
        public async Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode)
        {
            var sessionContext = await CreateSessionContextAsync(context);

            if (!sessionContext)
            {
                return new PutResult(sessionContext, contentHash);
            }

            return await PutFileAsync(sessionContext.Value, contentHash, path, realizationMode);
        }
        
        private Task<PutResult> PutFileAsync(
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
                    }),
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
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode)
        {
            var sessionContext = await CreateSessionContextAsync(context);
            if (!sessionContext)
            {
                return new PutResult(sessionContext, new ContentHash(hashType));
            }

            return await PutFileAsync(sessionContext.Value, hashType, path, realizationMode);
        }

        private Task<PutResult> PutFileAsync(
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
                    }),
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
        public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream)
        {
            return PutStreamInternalAsync(
                context,
                stream,
                contentHash,
                (sessionContext, tempFile) => PutFileAsync(sessionContext, contentHash, tempFile, FileRealizationMode.HardLink));
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream)
        {
            return PutStreamInternalAsync(
                context,
                stream,
                new ContentHash(hashType),
                (sessionContext, tempFile) => PutFileAsync(sessionContext, hashType, tempFile, FileRealizationMode.HardLink));
        }

        /// <inheritdoc />
        public async Task<DeleteResult> DeleteContentAsync(Context context, ContentHash hash)
        {
            try
            {
                DeleteContentRequest request = new DeleteContentRequest()
                {
                    TraceId = context.Id.ToString(),
                    HashType = (int)hash.HashType,
                    ContentHash = hash.ToByteString()
                };

                DeleteContentResponse response = await Client.DeleteAsync(request);
                if (response.Header.Succeeded)
                {
                    return new DeleteResult((DeleteResult.ResultCode)response.Result, hash, response.EvictedSize, response.PinnedSize);
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

        private async Task<PutResult> PutStreamInternalAsync(Context context, Stream stream, ContentHash contentHash, Func<SessionContext, AbsolutePath, Task<PutResult>> putFileFunc)
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

                using (var fileStream = await FileSystem.OpenAsync(tempFile, FileAccess.Write, FileMode.Create, FileShare.Delete))
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
        protected override AsyncUnaryCall<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest heartbeatRequest)
        {
            return Client.HeartbeatAsync(heartbeatRequest);
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
