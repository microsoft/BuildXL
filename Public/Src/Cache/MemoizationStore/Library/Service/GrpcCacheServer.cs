// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Service;
using BuildXL.Utilities.Tracing;
using ContentStore.Grpc;
using Grpc.Core;

// Can't rename ProtoBuf

namespace BuildXL.Cache.MemoizationStore.Sessions.Grpc
{
    /// <summary>
    /// A cache server implementation based on GRPC.
    /// </summary>
    public sealed class GrpcCacheServer : GrpcContentServer
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(GrpcCacheServer));

        /// <summary>
        /// We need this because these methods rely on having memoization verbs, which only an ICacheSession can
        /// provide.
        /// </summary>
        private readonly ISessionHandler<ICacheSession> _cacheSessionHandler;

        /// <nodoc />
        public GrpcCacheServer(
            ILogger logger,
            Capabilities serviceCapabilities,
            ISessionHandler<ICacheSession> sessionHandler,
            Dictionary<string, IContentStore> storesByName,
            LocalServerConfiguration localServerConfiguration = null)
            : base(logger, serviceCapabilities, sessionHandler, storesByName, localServerConfiguration)
        {
            _cacheSessionHandler = sessionHandler;

            GrpcAdapter = new MemoizationServerAdapter(this);
        }

        private Task<AddOrGetContentHashListResponse> AddOrGetContentHashListAsync(AddOrGetContentHashListRequest request, ServerCallContext context)
        {
            return PerformOperationAsync(
                request,
                async c =>
                {
                    StrongFingerprint fingerprint = request.Fingerprint.FromGrpc();
                    ContentHashListWithDeterminism contentHashListWithDeterminism = request.HashList.FromGrpc();
                    var result = await c.Session.AddOrGetContentHashListAsync(c.Context, fingerprint, contentHashListWithDeterminism, c.Context.Token);

                    return new AddOrGetContentHashListResponse()
                    {
                        Header = result.Succeeded ?
                            ResponseHeader.Success(c.StartTime) : 
                            ResponseHeader.Failure(c.StartTime, (int)result.Code, result.ErrorMessage, result.Diagnostics),
                        HashList = result.ContentHashListWithDeterminism.ToGrpc(),
                    };
                },
                context.CancellationToken);
        }

        private Task<GetContentHashListResponse> GetContentHashListAsync(GetContentHashListRequest request, ServerCallContext context)
        {
            return PerformOperationAsync(
                request,
                async c =>
                {
                    StrongFingerprint fingerprint = request.Fingerprint.FromGrpc();
                    var result = await c.Session.GetContentHashListAsync(c.Context, fingerprint, c.Context.Token).ThrowIfFailure();

                    return new GetContentHashListResponse()
                    {
                        HashList = result.ContentHashListWithDeterminism.ToGrpc(),
                    };
                },
                context.CancellationToken);
        }

        private Task<GetSelectorsResponse> GetSelectorsAsync(GetSelectorsRequest request, ServerCallContext context)
        {
            return PerformOperationAsync(
                request,
                async c =>
                {
                    Fingerprint fingerprint = request.WeakFingerprint.DeserializeFingerprintFromGrpc();
                    if (c.Session is IReadOnlyMemoizationSessionWithLevelSelectors withSelectors)
                    {
                        var result = await withSelectors.GetLevelSelectorsAsync(c.Context, fingerprint, c.Context.Token, request.Level).ThrowIfFailure();
                        var selectors = result.Value.Selectors.Select(s => s.ToGrpc());
                        return new GetSelectorsResponse(result.Value.HasMore, selectors);
                    }

                    throw new NotSupportedException($"Session {c.Session.GetType().Name} does not support {nameof(GetSelectorsAsync)} functionality.");
                },
                context.CancellationToken);
        }

        private Task<IncorporateStrongFingerprintsResponse> IncorporateStrongFingerprints(IncorporateStrongFingerprintsRequest request, ServerCallContext context)
        {
            return PerformOperationAsync(
                request,
                async c =>
                {
                    IEnumerable<StrongFingerprint> strongFingerprints = GrpcDataConverter.FromGrpc(request.StrongFingerprints.ToArray());
                    var result = await c.Session.IncorporateStrongFingerprintsAsync(c.Context, strongFingerprints.Select(f => Task.FromResult(f)), c.Context.Token).ThrowIfFailure();

                    return new IncorporateStrongFingerprintsResponse();
                },
                context.CancellationToken);
        }

        private async Task<TResponse> PerformOperationAsync<TResponse>(
            IGrpcRequest request,
            Func<ServiceOperationContext, Task<TResponse>> taskFunc,
            CancellationToken token,
            [CallerMemberName]string operation = null!) where TResponse : IGrpcResponse, new()
        {
            var sw = StopwatchSlim.Start();

            DateTime startTime = DateTime.UtcNow;
            
            using var shutdownTracker = TrackShutdown(new OperationContext(new Context(request.Header.TraceId, Logger), token), token);
            var tracingContext = shutdownTracker.Context;
            
            var sessionId = request.Header.SessionId;
            if (!_cacheSessionHandler.TryGetSession(sessionId, out var session))
            {
                return failure($"Could not find session for session ID {sessionId}");
            }

            await Task.Yield();

            try
            {
                var serviceOperationContext = new ServiceOperationContext(session, tracingContext, startTime);

                TraceGrpcOperationStarted(tracingContext, enabled: TraceGrpcOperations, operation, sessionId);

                var result = await taskFunc(serviceOperationContext);
                
                if (result.Header == null)
                {
                    result.Header = ResponseHeader.Success(startTime);
                }

                TraceGrpcOperationFinished(tracingContext, enabled: TraceGrpcOperations, operation, sw.Elapsed, sessionId);

                return result;
            }
            catch (TaskCanceledException e)
            {
                var message = GetLogMessage(e, operation, sessionId);
                Tracer.OperationFinished(tracingContext, FromException(e), sw.Elapsed, message, operation);
                return failure(message);
            }
            catch (ResultPropagationException e)
            {
                var message = GetLogMessage(e, operation, sessionId);
                Tracer.OperationFinished(tracingContext, FromException(e), sw.Elapsed, message, operation);
                return new TResponse {Header = ResponseHeader.Failure(startTime, e.Result.ErrorMessage, e.Result.Diagnostics)};
            }
            catch (Exception e)
            {
                var message = GetLogMessage(e, operation, sessionId);
                Tracer.OperationFinished(tracingContext, FromException(e), sw.Elapsed, message, operation);
                return failure(e.ToString());
            }

            TResponse failure(string errorMessage) => new TResponse { Header = ResponseHeader.Failure(startTime, errorMessage) };
        }

        internal readonly struct ServiceOperationContext
        {
            public IMemoizationSession Session { get; }

            public OperationContext Context { get; }

            public DateTime StartTime { get; }

            /// <inheritdoc />
            public ServiceOperationContext(IMemoizationSession session, OperationContext context, DateTime startTime)
                : this()
            {
                Session = session;
                Context = context;
                StartTime = startTime;
            }
        }

        private class MemoizationServerAdapter : ContentServerAdapter
        {
            private readonly GrpcCacheServer _server;

            /// <inheritdoc />
            public MemoizationServerAdapter(GrpcCacheServer server) : base(server) => _server = server;

            /// <inheritdoc />
            public override Task<AddOrGetContentHashListResponse> AddOrGetContentHashList(AddOrGetContentHashListRequest request, ServerCallContext context) => _server.AddOrGetContentHashListAsync(request, context);

            /// <inheritdoc />
            public override Task<GetContentHashListResponse> GetContentHashList(GetContentHashListRequest request, ServerCallContext context) => _server.GetContentHashListAsync(request, context);

            /// <inheritdoc />
            public override Task<GetSelectorsResponse> GetSelectors(GetSelectorsRequest request, ServerCallContext context) => _server.GetSelectorsAsync(request, context);

            /// <inheritdoc />
            public override Task<IncorporateStrongFingerprintsResponse> IncorporateStrongFingerprints(IncorporateStrongFingerprintsRequest request, ServerCallContext context) => _server.IncorporateStrongFingerprints(request, context);
        }
    }
}
