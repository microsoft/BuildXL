// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Timers;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using ContentStore.Grpc;
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Base implementation of service client based on GRPC.
    /// </summary>
    public abstract class GrpcClientBase : StartupShutdownSlimBase
    {
        private const string HeartbeatName = "Heartbeat";
        private const int DefaultHeartbeatIntervalMinutes = 1;

        private readonly int _grpcPort;
        private readonly TimeSpan _heartbeatInterval;
        private Capabilities _clientCapabilities;
        private Capabilities _serviceCapabilities;
        private IntervalTimer _heartbeatTimer;

        private bool _serviceUnavailable;

        /// <nodoc />
        protected readonly Channel Channel;

        /// <nodoc />
        protected readonly IAbsFileSystem FileSystem;

        /// <nodoc />
        protected readonly string Scenario;

        /// <nodoc />
        protected readonly ServiceClientContentSessionTracer ServiceClientTracer;

        /// <nodoc />
        protected SessionState SessionState;

        /// <inheritdoc />
        protected override Tracer Tracer => ServiceClientTracer;

        /// <nodoc />
        protected GrpcClientBase(
            IAbsFileSystem fileSystem,
            ServiceClientContentSessionTracer tracer,
            int grpcPort,
            string scenario,
            Capabilities clientCapabilities,
            TimeSpan? heartbeatInterval = null)
        {
            FileSystem = fileSystem;
            ServiceClientTracer = tracer;
            _grpcPort = grpcPort;
            Scenario = scenario;

            GrpcEnvironment.InitializeIfNeeded();
            Channel = new Channel(GrpcEnvironment.Localhost, (int)grpcPort, ChannelCredentials.Insecure, GrpcEnvironment.DefaultConfiguration);
            _clientCapabilities = clientCapabilities;
            _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromMinutes(DefaultHeartbeatIntervalMinutes);
        }

        /// <nodoc />
        public void Dispose()
        {
            _heartbeatTimer?.Dispose();
            SessionState?.Dispose();
        }

        /// <nodoc />
        protected abstract AsyncUnaryCall<ShutdownResponse> ShutdownSessionAsync(ShutdownRequest shutdownRequest);

        /// <nodoc />
        protected abstract AsyncUnaryCall<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest heartbeatRequest);

        /// <nodoc />
        protected abstract AsyncUnaryCall<HelloResponse> HelloAsync(HelloRequest helloRequest, CancellationToken token);

        /// <nodoc />
        protected abstract AsyncUnaryCall<GetStatsResponse> GetStatsAsync(GetStatsRequest getStatsRequest);

        /// <nodoc />
        protected abstract AsyncUnaryCall<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest createSessionRequest);

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            try
            {
                _heartbeatTimer?.Dispose();

                if (SessionState == null)
                {
                    // _sessionState is null if initialization has failed.
                    return BoolResult.Success;
                }

                var sessionContext = await CreateSessionContextAsync(context, context.Token);
                if (!sessionContext)
                {
                    return new BoolResult(sessionContext);
                }

                if (_serviceUnavailable)
                {
                    context.TracingContext.Debug("Skipping session shutdown because service is unavailable.");
                }
                else
                {
                    try
                    {
                        await ShutdownSessionAsync(new ShutdownRequest {Header = sessionContext.Value.CreateHeader() });
                    }
                    catch (RpcException e)
                    {
                        context.TracingContext.Error($"Failed to shut down session with error: {e}");
                    }
                }

                await Channel.ShutdownAsync();
                return BoolResult.Success;
            }
            catch (Exception ex)
            {
                // Catching all exceptions, even ClientCanRetryExceptions, because the teardown steps aren't idempotent.
                // In the worst case, the shutdown call comes while the service is offline, the service never receives it,
                // and then the service times out the session 10 minutes later (by default).
                return new BoolResult(ex);
            }
        }

        private async Task SendHeartbeatAsync(Context context)
        {
            var sessionContext = await CreateSessionContextAsync(context);
            if (!sessionContext)
            {
                // We do not even attempt to send a heartbeat if we can't get a session ID.
                return;
            }

            try
            {
                HeartbeatResponse response = await HeartbeatAsync(new HeartbeatRequest { Header = sessionContext.Value.CreateHeader() });

                // Check for null header here as a workaround to a known service bug, which returns null headers on successful heartbeats.
                if (response?.Header != null && !response.Header.Succeeded)
                {
                    Tracer.Warning(
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
                Tracer.Debug(context, message);
            }
        }

        /// <nodoc />
        public async Task<BoolResult> CreateSessionAsync(
            Context context,
            string name,
            string cacheName,
            ImplicitPin implicitPin)
        {
            var startupResult = await StartupAsync(context, 5000);
            if (!startupResult)
            {
                return startupResult;
            }

            CreateSessionResponse response = await CreateSessionAsyncInternalAsync(context, name, cacheName, implicitPin);
            if (string.IsNullOrEmpty(response.ErrorMessage))
            {
                SessionData data = new SessionData { SessionId = response.SessionId, TemporaryDirectory = new DisposableDirectory(FileSystem, new AbsolutePath(response.TempDirectory)) };
                SessionState = new SessionState(() => CreateSessionDataAsync(context, name, cacheName, implicitPin), data);

                // Send a heartbeat iff both the service can receive one and the service was told to expect one.
                if ((_serviceCapabilities & Capabilities.Heartbeat) != 0 &&
                    (_clientCapabilities & Capabilities.Heartbeat) != 0)
                {
                    _heartbeatTimer = new IntervalTimer(() => SendHeartbeatAsync(context), _heartbeatInterval, message =>
                    {
                        Tracer.Debug(context, $"[{HeartbeatName}] {message}");
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
        public override Task<BoolResult> StartupAsync(Context context) => StartupAsync(context, 5000);

        /// <nodoc />
        public async Task<BoolResult> StartupAsync(Context context, int waitMs)
        {
            try
            {
                context.Always($"Starting up GRPC client against service on port {_grpcPort} with timeout {waitMs}.");

                if (!LocalContentServer.EnsureRunning(context, Scenario, waitMs))
                {
                    throw new ClientCanRetryException(context, $"{nameof(GrpcContentClient)} failed to detect running service for scenario '{Scenario}' during startup.");
                }

                HelloResponse helloResponse;
                using (var ct = new CancellationTokenSource(waitMs > 0 ? waitMs : Timeout.Infinite))
                {
                    helloResponse = await SendGrpcRequestAsync(
                        context,
                        async () => await HelloAsync(new HelloRequest(), ct.Token));
                }

                if (helloResponse.Success)
                {
                    _serviceCapabilities = (Capabilities) helloResponse.Capabilities;
                    CheckCompatibility(_clientCapabilities, _serviceCapabilities).ThrowIfFailure();
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

        /// <nodoc />
        public static BoolResult CheckCompatibility(Capabilities clientCapabilities, Capabilities serviceCapabilities)
        {
            var requiredClientCapabilities = clientCapabilities & Capabilities.RequiredCapabilitiesMask;
            var serviceRequiredCapabilities = serviceCapabilities & Capabilities.RequiredCapabilitiesMask;

            if (requiredClientCapabilities > serviceRequiredCapabilities)
            {
                return new BoolResult($"Required client's capabilities ({requiredClientCapabilities}) don't match service's capabilities ({serviceRequiredCapabilities}).");
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
                SessionData data = new SessionData() { SessionId = response.SessionId, TemporaryDirectory = new DisposableDirectory(FileSystem, new AbsolutePath(response.TempDirectory))};
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
            Func<Task<CreateSessionResponse>> func = async () => await CreateSessionAsync(
                            new CreateSessionRequest
                            {
                                CacheName = cacheName,
                                SessionName = name,
                                ImplicitPin = (int)implicitPin,
                                TraceId = context.Id.ToString(),
                                Capabilities = (int)_clientCapabilities
                            });
            CreateSessionResponse response = await SendGrpcRequestAsync(context, func);
            return response;
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
                var response = await SendGrpcRequestAsync(context, async () => await GetStatsAsync(new GetStatsRequest()));
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

        /// <nodoc />
        protected async Task<Result<SessionContext>> CreateSessionContextAsync(Context context, CancellationToken token = default)
        {
            ObjectResult<SessionData> result = await SessionState.GetDataAsync();
            if (!result)
            {
                return Result.FromError<SessionContext>(result);
            }

            var startTime = DateTime.UtcNow;
            return new SessionContext(context, startTime, result.Data, token);
        }

        /// <summary>
        /// Performs an rpc operation.
        /// </summary>
        protected async Task<TResult> PerformOperationAsync<TResult, TResponse>(
            OperationContext context,
            Func<SessionContext, AsyncUnaryCall<TResponse>> func,
            Func<TResponse, TResult> responseHandler)
            where TResult : ResultBase
            where TResponse : IGrpcResponse
        {
            var sessionContext = await CreateSessionContextAsync(context, context.Token);
            if (!sessionContext)
            {
                return new ErrorResult(sessionContext).AsResult<TResult>();
            }

            return await PerformOperationAsync(sessionContext.Value, func, responseHandler);
        }

        /// <summary>
        /// Performs an rpc operation.
        /// </summary>
        protected Task<TResult> PerformOperationAsync<TResult, TResponse>(
            SessionContext context,
            Func<SessionContext, AsyncUnaryCall<TResponse>> func,
            Func<TResponse, TResult> responseHandler)
            where TResult : ResultBase
            where TResponse : IGrpcResponse
        {
            return SendGrpcRequestAsync(
                context.TracingContext,
                async () =>
                {
                    var response = await func(context);
                    await ProcessResponseHeaderAsync(context, response, throwFailures: false);
                    return responseHandler(response);
                });
        }

        /// <nodoc />
        protected async Task<TResult> PerformOperationAsync<TResult>(OperationContext context, Func<SessionContext, Task<TResult>> operation) where TResult : ResultBase
        {
            var sessionContext = await CreateSessionContextAsync(context, context.Token);

            if (!sessionContext)
            {
                return new ErrorResult(sessionContext).AsResult<TResult>();
            }

            try
            {
                return await operation(sessionContext.Value);
            }
            catch (ResultPropagationException error)
            {
                return new ErrorResult(error).AsResult<TResult>();
            }
            catch (Exception e) when (!(e is ClientCanRetryException))
            {
                return new ErrorResult(e).AsResult<TResult>();
            }
        }

        /// <summary>
        /// Tracks a latency of a server's response and throws an exception if <paramref name="responseHeader"/>'s Success property returns false.
        /// </summary>
        private void TrackLatencyAndThrowIfFailure(DateTime startTime, ResponseHeader responseHeader, bool throwFailures)
        {
            Contract.Assert(responseHeader != null);

            var serverTicks = responseHeader.ServerReceiptTimeUtcTicks;
            if (serverTicks > 0 && serverTicks > startTime.Ticks)
            {
                // It make no sense to trace negative numbers.
                // It is possible that the time on different machines is different and the server time is less then the local one.
                long ticksWaited = serverTicks - startTime.Ticks;
                ServiceClientTracer.TrackClientWaitForServerTicks(ticksWaited);
            }

            if (!responseHeader.Succeeded && throwFailures)
            {
                var errorResult = new ErrorResult(responseHeader.ErrorMessage, responseHeader.Diagnostics);
                // This method will throw ResultPropagationException that is tracked by the PerformSessionOperationAsync
                errorResult.ThrowIfFailure();
            }
        }

        /// <nodoc />
        protected async Task<T> SendGrpcRequestAsync<T>(Context context, Func<Task<T>> func)
        {
            try
            {
                var result = await func();
                _serviceUnavailable = false;

                return result;
            }
            catch (RpcException ex)
            {
                if (ex.Status.StatusCode == StatusCode.Unavailable)
                {
                    // If the service is unavailable we can save time by not shutting down the service gracefully.
                    // TODO ST: I think it should be easier to track successful state, not an error state!
                    _serviceUnavailable = true;
                    throw new ClientCanRetryException(context, $"{nameof(GrpcContentClient)} failed to detect running service while running client action");
                }

                throw new ClientCanRetryException(context, ex.ToString(), ex);
            }
        }

        /// <nodoc />
        protected Task<T> SendGrpcRequestAndThrowIfFailedAsync<T>(SessionContext context, Func<Task<T>> func, bool throwFailures = true) where T : IGrpcResponse
        {
            return SendGrpcRequestAsync(
                context.TracingContext,
                async () =>
                {
                    var result = await func();

                    await ProcessResponseHeaderAsync(context, result, throwFailures);

                    return result;
                });
        }

        /// <summary>
        /// Process response header from a given <paramref name="result"/> and throw an exception if the result is unsuccessful and <paramref name="throwFailures"/> is true.
        /// </summary>
        protected async Task ProcessResponseHeaderAsync<T>(SessionContext context, T result, bool throwFailures)
            where T : IGrpcResponse
        {
            if (result.Header != null)
            {
                long ticksWaited = result.Header.ServerReceiptTimeUtcTicks - context.StartTime.Ticks;
                ServiceClientTracer.TrackClientWaitForServerTicks(ticksWaited);

                TrackLatencyAndThrowIfFailure(context.StartTime, result.Header, throwFailures);

                await ResetOnUnknownSessionAsync(context.TracingContext, result.Header, context.SessionId);
            }
        }

        /// <summary>
        /// Test hook for overriding the client's capabilities
        /// </summary>
        internal void DisableCapabilities(Capabilities capabilities)
        {
            _clientCapabilities &= ~capabilities;
        }

        /// <nodoc />
        protected async Task ResetOnUnknownSessionAsync(Context context, ResponseHeader header, int sessionId)
        {
            // At the moment, the error message is the only way to identify that the error was due to
            // the session ID being unknown to the server.
            if (!header.Succeeded && header.ErrorMessage.Contains("Could not find session"))
            {
                await SessionState.ResetAsync(sessionId);
                throw new ClientCanRetryException(context, $"Could not find session id {sessionId}");
            }
        }

        /// <summary>
        /// Context for handling session requests.
        /// </summary>
        public readonly struct SessionContext
        {
            /// <nodoc />
            public Context TracingContext { get; }

            /// <nodoc />
            public CancellationToken Token { get; }

            /// <summary>
            /// Time when the operation has started.
            /// </summary>
            public DateTime StartTime { get; }

            /// <nodoc />
            public int SessionId => SessionData.SessionId;

            /// <nodoc />
            public SessionData SessionData { get; }

            /// <nodoc />
            public SessionContext(Context tracingContext, DateTime startTime, SessionData sessionData, CancellationToken token)
            {
                TracingContext = tracingContext;
                Token = token;
                StartTime = startTime;
                SessionData = sessionData;
            }

            /// <nodoc />
            public RequestHeader CreateHeader() => new RequestHeader(TracingContext.Id, SessionId);
        }

    }
}
