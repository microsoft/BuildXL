// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using ContentStore.Grpc; // Can't rename ProtoBuf
using Grpc.Core;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Service;

namespace BuildXL.Cache.MemoizationStore.Sessions.Grpc
{
    /// <summary>
    /// A cache server implementation based on GRPC.
    /// </summary>
    public sealed class GrpcCacheServer : IGrpcService
    {
        private readonly ILogger _logger;
        private readonly ISessionHandler<ICacheSession> _sessionHandler;

        private readonly MemoizationServerAdapter _adapter;
        
        /// <nodoc />
        public GrpcCacheServer(
            ILogger logger,
            ISessionHandler<ICacheSession> sessionHandler)
        {
            _logger = logger;
            _sessionHandler = sessionHandler;

            _adapter = new MemoizationServerAdapter(this);
        }

        /// <inheritdoc />
        public ServerServiceDefinition Bind() => CacheServer.BindService(_adapter);

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

                    throw new NotSupportedException($"Session {c.Session.GetType().Name} does not support GetSelectosAsync functionality.");
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
            [CallerMemberName]string operation = null) where TResponse : IGrpcResponse, new()
        {
            var stopwatch = StopwatchSlim.Start();
            DateTime startTime = DateTime.UtcNow;
            var cacheContext = new OperationContext(new Context(new Guid(request.Header.TraceId), _logger), token);

            var sessionId = request.Header.SessionId;
            if (!_sessionHandler.TryGetSession(sessionId, out var session))
            {
                return failure($"Could not find session for session ID {sessionId}");
            }

            await Task.Yield();

            try
            {
                var serviceOperationContext = new ServiceOperationContext(session, cacheContext, startTime);

                var result = await taskFunc(serviceOperationContext);
                
                if (result.Header == null)
                {
                    result.Header = ResponseHeader.Success(startTime);
                }

                _logger.Info($"GRPC server operation '{operation}' succeeded by {stopwatch.Elapsed.TotalMilliseconds}ms.");
                return result;
            }
            catch (TaskCanceledException)
            {
                _logger.Info($"GRPC server operation '{operation}' is canceled by {stopwatch.Elapsed.TotalMilliseconds}ms.");
                return failure("The operation was canceled.");
            }
            catch (ResultPropagationException e)
            {
                _logger.Error(e, $"GRPC server operation '{operation}' failed by {stopwatch.Elapsed.TotalMilliseconds}ms. Error: {e}");

                return new TResponse {Header = ResponseHeader.Failure(startTime, e.Result.ErrorMessage, e.Result.Diagnostics)};
            }
            catch (Exception e)
            {
                _logger.Error(e, $"GRPC server operation '{operation}' failed by {stopwatch.Elapsed.TotalMilliseconds}ms. Error: {e}");
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

        private class MemoizationServerAdapter : CacheServer.CacheServerBase
        {
            private readonly GrpcCacheServer _server;

            /// <inheritdoc />
            public MemoizationServerAdapter(GrpcCacheServer server) => _server = server;

            public override Task<AddOrGetContentHashListResponse> AddOrGetContentHashList(AddOrGetContentHashListRequest request, ServerCallContext context)
            {
                return _server.AddOrGetContentHashListAsync(request, context);
            }

            public override Task<GetContentHashListResponse> GetContentHashList(GetContentHashListRequest request, ServerCallContext context)
            {
                return _server.GetContentHashListAsync(request, context);
            }

            public override Task<GetSelectorsResponse> GetSelectors(GetSelectorsRequest request, ServerCallContext context)
            {
                return _server.GetSelectorsAsync(request, context);
            }

            public override Task<IncorporateStrongFingerprintsResponse> IncorporateStrongFingerprints(IncorporateStrongFingerprintsRequest request, ServerCallContext context)
            {
                return _server.IncorporateStrongFingerprints(request, context);
            }
        }
    }
}
