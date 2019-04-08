// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Timers;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStore.Grpc; // Can't rename ProtoBuf
using Google.Protobuf;
using Grpc.Core;
using HelloRequest = ContentStore.Grpc.HelloRequest;
using HelloResponse = ContentStore.Grpc.HelloResponse;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// An implementation of a CAS service client based on GRPC.
    /// </summary>
    public sealed class GrpcClient : IRpcClient
    {
        private const Capabilities DefaultClientCapabilities = Capabilities.All;
        private const string HeartbeatName = "Heartbeat";
        private const int DefaultHeartbeatIntervalMinutes = 1;
        private readonly Channel _channel;
        private readonly ContentServer.ContentServerClient _client;
        private readonly string _scenario;
        private readonly ServiceClientContentSessionTracer _tracer;
        private readonly IAbsFileSystem _fileSystem;
        private readonly uint _grpcPort;
        private readonly TimeSpan _heartbeatInterval;
        private Capabilities _clientCapabilities;
        private Capabilities _serviceCapabilities;
        private IntervalTimer _heartbeatTimer;
        private SessionState _sessionState;

        private bool _serviceUnavailable;

        /// <summary>
        /// Initializes a new instance of the <see cref="GrpcClient" /> class.
        /// </summary>
        public GrpcClient(ServiceClientContentSessionTracer tracer, IAbsFileSystem fileSystem, uint grpcPort, string scenario, TimeSpan? heartbeatInterval = null)
        {
            _tracer = tracer;
            _fileSystem = fileSystem;
            _grpcPort = grpcPort;
            _scenario = scenario;
            GrpcEnvironment.InitializeIfNeeded();
            _channel = new Channel(GrpcEnvironment.Localhost, (int)grpcPort, ChannelCredentials.Insecure);
            _client = new ContentServer.ContentServerClient(_channel);
            _clientCapabilities = DefaultClientCapabilities;
            _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromMinutes(DefaultHeartbeatIntervalMinutes);
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public async Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            try
            {
                _heartbeatTimer?.Dispose();

                if (_sessionState == null)
                {
                    // _sessionState is null if initialization has failed.
                    return BoolResult.Success;
                }

                StructResult<int> sessionResult = await _sessionState.GetIdAsync();
                if (!sessionResult.Succeeded)
                {
                    return new BoolResult(sessionResult);
                }

                int sessionId = sessionResult.Data;

                if (_serviceUnavailable)
                {
                    context.Debug("Skipping session shutdown because service is unavailable.");
                }
                else
                {
                    try
                    {
                        await _client.ShutdownSessionAsync(new ShutdownRequest {Header = new RequestHeader(context.Id, sessionId)});
                    }
                    catch (RpcException e)
                    {
                        context.Error($"Failed to shut down session with error: {e}");
                    }
                }

                await _channel.ShutdownAsync();
                ShutdownCompleted = true;
                return BoolResult.Success;
            }
            catch (Exception ex)
            {
                // Catching all exceptions, even ClientCanRetryExceptions, because the teardown steps aren't idempotent.
                // In the worst case, the shutdown call comes while the service is offline, the service never receives it,
                // and then the service times out the session 10 minutes later (by default).
                ShutdownCompleted = true;
                return new BoolResult(ex);
            }
        }

        private async Task SendHeartbeatAsync(Context context)
        {
            StructResult<int> sessionResult = await _sessionState.GetIdAsync();
            if (!sessionResult.Succeeded)
            {
                // We do not even attempt to send a heartbeat if we can't get a session ID.
                return;
            }

            int sessionId = sessionResult.Data;

            try
            {
                HeartbeatResponse response = await _client.HeartbeatAsync(new HeartbeatRequest {Header = new RequestHeader(context.Id, sessionId) });

                // Check for null header here as a workaround to a known service bug, which returns null headers on successful heartbeats.
                if (response?.Header != null && !response.Header.Succeeded)
                {
                    _tracer.Warning(
                        context,
                        $"Heartbeat failed: ErrorMessage=[{response.Header.ErrorMessage}] Diagnostics=[{response.Header.Diagnostics}]");

                    // Nor do we attempt to reset a session ID based on a failed heartbeat.
                }
            }
            catch (Exception ex)
            {
                string message = (ex is RpcException rpcEx) && (rpcEx.Status.StatusCode == StatusCode.Unavailable)
                    ? "Heartbeat failed to detect running service."
                    : $"Heartbeat failed: [{ex}]";
                _tracer.Debug(context, message);
            }
        }

        /// <inheritdoc />
        public async Task<BoolResult> CreateSessionAsync(
            Context context,
            string name,
            string cacheName,
            ImplicitPin implicitPin)
        {
            var startupResult = await StartupAsync(context, 5000);
            if (!startupResult.Succeeded)
            {
                return startupResult;
            }

            CreateSessionResponse response = await CreateSessionAsyncInternalAsync(context, name, cacheName, implicitPin);
            if (string.IsNullOrEmpty(response.ErrorMessage))
            {
                Task<ObjectResult<SessionData>> sessionFactory() => CreateSessionDataAsync(context, name, cacheName, implicitPin);
                SessionData data = new SessionData { SessionId = response.SessionId, TemporaryDirectory = new DisposableDirectory(_fileSystem, new AbsolutePath(response.TempDirectory)) };
                _sessionState = new SessionState(sessionFactory, data);

                // Send a heartbeat iff both the service can receive one and the service was told to expect one.
                if ((_serviceCapabilities & Capabilities.Heartbeat) != 0 &&
                    (_clientCapabilities & Capabilities.Heartbeat) != 0)
                {
                    _heartbeatTimer = new IntervalTimer(() => SendHeartbeatAsync(context), _heartbeatInterval, message =>
                    {
                        _tracer.Debug(context, $"[{HeartbeatName}] {message}");
                    });
                }

                return new StructResult<int>(response.SessionId);
            }
            else
            {
                return new StructResult<int>(response.ErrorMessage);
            }
        }

        /// <nodoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            return StartupAsync(context, 5000);
        }

        /// <nodoc />
        public async Task<BoolResult> StartupAsync(Context context, int waitMs)
        {
            try
            {
                if (!LocalContentServer.EnsureRunning(context, _scenario, waitMs))
                {
                    throw new ClientCanRetryException(context, $"{nameof(GrpcClient)} failed to detect running service for scenario {_scenario} during startup");
                }

                context.Always($"Starting up GRPC client against service on port {_grpcPort}");

                HelloResponse helloResponse;
                using (var ct = new CancellationTokenSource(waitMs))
                {
                    helloResponse = await RunClientActionAndThrowIfFailedAsync(
                        context,
                        async () => await _client.HelloAsync(
                            new HelloRequest(),
                            cancellationToken: ct.Token));
                }

                if (helloResponse.Success)
                {
                    _serviceCapabilities = (Capabilities) helloResponse.Capabilities;
                }
                else
                {
                    return new BoolResult("Failed to connect to service for unknown reason.");
                }
            }
            catch (Exception ex) when (!(ex is ClientCanRetryException))
            {
                // The caller's retry policy needs to see ClientCanRetryExceptions in order to properly retry
                return new BoolResult(ex);
            }

            return BoolResult.Success;
        }

        private async Task<ObjectResult<SessionData>> CreateSessionDataAsync(
            Context context,
            string name,
            string cacheName,
            ImplicitPin implicitPin)
        {
            CreateSessionResponse response = await CreateSessionAsyncInternalAsync(context, name, cacheName, implicitPin);
            if (string.IsNullOrEmpty(response.ErrorMessage))
            {
                SessionData data = new SessionData() { SessionId = response.SessionId, TemporaryDirectory = new DisposableDirectory(_fileSystem, new AbsolutePath(response.TempDirectory))};
                return new ObjectResult<SessionData>(data);
            }
            else
            {
                return new ObjectResult<SessionData>(response.ErrorMessage);
            }
        }

        private async Task<CreateSessionResponse> CreateSessionAsyncInternalAsync(
            Context context,
            string name,
            string cacheName,
            ImplicitPin implicitPin)
        {
            CreateSessionResponse response = await RunClientActionAndThrowIfFailedAsync(context, async () => await _client.CreateSessionAsync(
              new CreateSessionRequest
              {
                  CacheName = cacheName,
                  SessionName = name,
                  ImplicitPin = (int)implicitPin,
                  TraceId = context.Id.ToString(),
                  Capabilities = (int)_clientCapabilities
              }));
            return response;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _heartbeatTimer?.Dispose();
            _sessionState?.Dispose();
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash)
        {
            ObjectResult<SessionData> result = await _sessionState.GetDataAsync();
            if (!result.Succeeded)
            {
                return new OpenStreamResult(result);
            }

            int sessionId = result.Data.SessionId;
            AbsolutePath tempPath = result.Data.TemporaryDirectory.CreateRandomFileName();

            var placeFileResult = await PlaceFileAsync(
                context,
                contentHash,
                tempPath,
                FileAccessMode.ReadOnly,
                FileReplacementMode.None,
                FileRealizationMode.HardLink,
                sessionId);

            if (placeFileResult.Succeeded)
            {
                try
                {
                    Stream stream = await _fileSystem.OpenReadOnlyAsync(tempPath, FileShare.Delete | FileShare.Read);
                    if (stream == null)
                    {
                        throw CreateServiceMayHaveRestarted(context, $"Failed to open temp file {tempPath}.");
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
        public async Task<PinResult> PinAsync(Context context, ContentHash contentHash)
        {
            StructResult<int> sessionResult = await _sessionState.GetIdAsync();
            if (!sessionResult.Succeeded)
            {
                return new PinResult(sessionResult);
            }

            int sessionId = sessionResult.Data;

            DateTime startTime = DateTime.UtcNow;
            PinResponse response = await RunClientActionAndThrowIfFailedAsync(
                context,
                async () => await _client.PinAsync(
                    new PinRequest
                    {
                        HashType = (int)contentHash.HashType,
                        ContentHash = contentHash.ToByteString(),
                        Header = new RequestHeader(context.Id, sessionId)
                    }));

            long ticksWaited = response.Header.ServerReceiptTimeUtcTicks - startTime.Ticks;
            _tracer.TrackClientWaitForServerTicks(ticksWaited);

            await ResetOnUnknownSessionAsync(context, response.Header, sessionId);

            return UnpackPinResult(response.Header);
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
            const int batchSize = 500;
            foreach (var chunk in contentHashes.GetPages(batchSize))
            {
                pinTasks.Add(PinBatchAsync(context, i, chunk.ToList()));
                i += batchSize;
            }

            pinResults.AddRange((await Task.WhenAll(pinTasks)).SelectMany(pins => pins));
            return pinResults;
        }

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> PinBatchAsync(Context context, int baseIndex, IReadOnlyList<ContentHash> chunk)
        {
            StructResult<int> sessionResult = await _sessionState.GetIdAsync();
            if (!sessionResult.Succeeded)
            {
                PinResult pinResult = new PinResult(sessionResult);
                return chunk.Select((ContentHash h) => pinResult).AsIndexed().AsTasks();
            }

            int sessionId = sessionResult.Data;

            var pinResults = new List<Indexed<PinResult>>();
            var bulkPinRequest = new PinBulkRequest {Header = new RequestHeader(context.Id, sessionId)};
            foreach (var contentHash in chunk)
            {
                bulkPinRequest.Hashes.Add(
                    new ContentHashAndHashTypeData { HashType = (int)contentHash.HashType, ContentHash = contentHash.ToByteString() });
            }

            DateTime startTime = DateTime.UtcNow;
            PinBulkResponse underlyingBulkPinResponse = await RunClientActionAndThrowIfFailedAsync(
                context,
                async () => await _client.PinBulkAsync(bulkPinRequest));
            long ticksWaited = underlyingBulkPinResponse.Header.Values.First().ServerReceiptTimeUtcTicks - startTime.Ticks;
            _tracer.TrackClientWaitForServerTicks(ticksWaited);

            foreach (var response in underlyingBulkPinResponse.Header)
            {
                await ResetOnUnknownSessionAsync(context, response.Value, sessionId);
                pinResults.Add(UnpackPinResult(response.Value).WithIndex(response.Key + baseIndex));
            }

            _tracer.LogPinResults(context, pinResults.Select(r => chunk[r.Index - baseIndex]).ToList(), pinResults.Select(r => r.Item).ToList());

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
            StructResult<int> sessionResult = await _sessionState.GetIdAsync();
            if (!sessionResult.Succeeded)
            {
                return new PlaceFileResult(sessionResult);
            }

            int sessionId = sessionResult.Data;

            return await PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, sessionId);
        }

        private async Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            int sessionId)
        {
            var startTime = DateTime.UtcNow;
            PlaceFileResponse response = await RunClientActionAndThrowIfFailedAsync(context, async () => await _client.PlaceFileAsync(
                new PlaceFileRequest
                {
                    Header = new RequestHeader(context.Id, sessionId),
                    HashType = (int)contentHash.HashType,
                    ContentHash = contentHash.ToByteString(),
                    Path = path.Path,
                    FileAccessMode = (int)accessMode,
                    FileRealizationMode = (int)realizationMode,
                    FileReplacementMode = (int)replacementMode
                }));
            long ticksWaited = response.Header.ServerReceiptTimeUtcTicks - startTime.Ticks;
            _tracer.TrackClientWaitForServerTicks(ticksWaited);

            // Workaround: Handle the service returning negative result codes in error cases
            PlaceFileResult.ResultCode resultCode = response.Header.Result < 0
                ? PlaceFileResult.ResultCode.Error
                : (PlaceFileResult.ResultCode)response.Header.Result;
            if (!response.Header.Succeeded)
            {
                await ResetOnUnknownSessionAsync(context, response.Header, sessionId);
                var message = string.IsNullOrEmpty(response.Header.ErrorMessage)
                    ? resultCode.ToString()
                    : response.Header.ErrorMessage;
                return new PlaceFileResult(resultCode, message, response.Header.Diagnostics);
            }
            else
            {
                return new PlaceFileResult(resultCode, response.ContentSize);
            }
        }

        /// <inheritdoc />
        public async Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode)
        {
            StructResult<int> sessionResult = await _sessionState.GetIdAsync();
            if (!sessionResult.Succeeded)
            {
                return new PutResult(sessionResult, contentHash);
            }

            int sessionId = sessionResult.Data;
            return await PutFileAsync(context, contentHash, path, realizationMode, sessionId);
        }

        /// <summary>
        /// Gets the statistics from the remote.
        /// </summary>
        public async Task<GetStatsResult> GetStatsAsync(Context context)
        {
            CounterSet counters = new CounterSet();

            // Get stats iff compatible with service and client
            if ((_serviceCapabilities & Capabilities.GetStats) != 0 &&
                (_clientCapabilities & Capabilities.GetStats) != 0)
            {
                var response = await RunClientActionAndThrowIfFailedAsync(context, async () => await _client.GetStatsAsync(new GetStatsRequest()));
                if (response.Success)
                {
                    foreach (var entry in response.Statistics)
                    {
                        counters.Add(entry.Key, entry.Value);
                    }
                }
            }

            return new GetStatsResult(counters);
        }

        private async Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            int sessionId)
        {
            DateTime startTime = DateTime.UtcNow;
            PutFileResponse response = await RunClientActionAndThrowIfFailedAsync(context, async () => await _client.PutFileAsync(
                   new PutFileRequest
                   {
                       Header = new RequestHeader(context.Id, sessionId),
                       ContentHash = contentHash.ToByteString(),
                       HashType = (int)contentHash.HashType,
                       FileRealizationMode = (int)realizationMode,
                       Path = path.Path
                   }));
            long ticksWaited = response.Header.ServerReceiptTimeUtcTicks - startTime.Ticks;
            _tracer.TrackClientWaitForServerTicks(ticksWaited);

            if (!response.Header.Succeeded)
            {
                await ResetOnUnknownSessionAsync(context, response.Header, sessionId);
                return new PutResult(contentHash, response.Header.ErrorMessage, response.Header.Diagnostics);
            }
            else
            {
                return new PutResult(response.ContentHash.ToContentHash((HashType)response.HashType), response.ContentSize);
            }
        }

        /// <inheritdoc />
        public async Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode)
        {
            StructResult<int> sessionResult = await _sessionState.GetIdAsync();
            if (!sessionResult.Succeeded)
            {
                return new PutResult(sessionResult, new ContentHash(hashType));
            }

            int sessionId = sessionResult.Data;
            return await PutFileAsync(context, hashType, path, realizationMode, sessionId);
        }

        private async Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            int sessionId)
        {
            DateTime startTime = DateTime.UtcNow;
            PutFileResponse response = await RunClientActionAndThrowIfFailedAsync(context, async () => await _client.PutFileAsync(
                new PutFileRequest
                {
                    Header = new RequestHeader(context.Id, sessionId),
                    ContentHash = ByteString.Empty,
                    HashType = (int)hashType,
                    FileRealizationMode = (int)realizationMode,
                    Path = path.Path
                }));
            long ticksWaited = response.Header.ServerReceiptTimeUtcTicks - startTime.Ticks;
            _tracer.TrackClientWaitForServerTicks(ticksWaited);

            if (!response.Header.Succeeded)
            {
                await ResetOnUnknownSessionAsync(context, response.Header, sessionId);
                return new PutResult(new ContentHash(hashType), response.Header.ErrorMessage, response.Header.Diagnostics);
            }
            else
            {
                return new PutResult(response.ContentHash.ToContentHash((HashType)response.HashType), response.ContentSize);
            }
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream)
        {
            return PutStreamInternalAsync(
                context,
                stream,
                contentHash,
                (sessionId, tempFile) => PutFileAsync(context, contentHash, tempFile, FileRealizationMode.HardLink, sessionId));
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream)
        {
            return PutStreamInternalAsync(
                context,
                stream,
                new ContentHash(hashType),
                (sessionId, tempFile) => PutFileAsync(context, hashType, tempFile, FileRealizationMode.HardLink, sessionId));
        }

        private async Task<PutResult> PutStreamInternalAsync(Context context, Stream stream, ContentHash contentHash, Func<int, AbsolutePath, Task<PutResult>> putFileFunc)
        {
            ObjectResult<SessionData> result = await _sessionState.GetDataAsync();
            if (!result.Succeeded)
            {
                return new PutResult(result, contentHash);
            }

            int sessionId = result.Data.SessionId;
            var tempFile = result.Data.TemporaryDirectory.CreateRandomFileName();
            try
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                using (var fileStream = await _fileSystem.OpenAsync(tempFile, FileAccess.Write, FileMode.Create, FileShare.Delete))
                {
                    if (fileStream == null)
                    {
                        throw CreateServiceMayHaveRestarted(context, $"Could not create temp file {tempFile}.");
                    }

                    await stream.CopyToAsync(fileStream);
                }

                PutResult putResult = await putFileFunc(sessionId, tempFile);

                if (putResult.Succeeded)
                {
                    return new PutResult(putResult.ContentHash, putResult.ContentSize);
                }
                else if (!_fileSystem.FileExists(tempFile))
                {
                    throw CreateServiceMayHaveRestarted(context, $"Temp file {tempFile} not found.");
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

        private static ClientCanRetryException CreateServiceMayHaveRestarted(Context context, string baseMessage)
        {
            // This is a very important logic today:
            // The service creates a temp directory for every session and it deletes all of them during shutdown
            // and recreates when when it loads the hibernated sessions.
            // This case is usually manifested via 'null' returned from FileSystem.OpenAsync because the file or part of the path is gone.
            // This is recoverable error and the client of this code should try again later, because when the service is back
            // it recreates all the temp directories for all the pending sessions back.
            return new ClientCanRetryException(context, $"{baseMessage} The service may have restarted.");
        }

        private async Task<T> RunClientActionAndThrowIfFailedAsync<T>(Context context, Func<Task<T>> func)
        {
            try
            {
                var result = await func();
                _serviceUnavailable = false;
                await Task.Yield();
                return result;
            }
            catch (RpcException ex)
            {
                if (ex.Status.StatusCode == StatusCode.Unavailable)
                {
                    // If the service is unavailable we can save time by not shutting down the service gracefully.
                    _serviceUnavailable = true;
                    throw new ClientCanRetryException(context, $"{nameof(GrpcClient)} failed to detect running service at port {_grpcPort} while running client action. [{ex}]");
                }

                throw new ClientCanRetryException(context, ex.ToString(), ex);
            }
        }

        /// <summary>
        /// Test hook for overriding the client's capabilities
        /// </summary>
        internal void DisableCapabilities(Capabilities capabilities)
        {
            _clientCapabilities &= ~capabilities;
        }

        private async Task ResetOnUnknownSessionAsync(Context context, ResponseHeader header, int sessionId)
        {
            // At the moment, the error message is the only way to identify that the error was due to
            // the session ID being unknown to the server.
            if (!header.Succeeded && header.ErrorMessage.Contains("Could not find session"))
            {
                await _sessionState.ResetAsync(sessionId);
                throw new ClientCanRetryException(context, $"Could not find session id {sessionId}");
            }
        }
    }
}
