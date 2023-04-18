// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core.Tracing;
using Grpc.Core;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService;

public abstract class GrpcCodeFirstClient<TClient> : StartupShutdownComponentBase
{
    private readonly IClientAccessor<TClient> _accessor;
    private readonly IRetryPolicy _retryPolicy;
    private readonly IClock _clock;
    private readonly TimeSpan _operationTimeout;

    protected GrpcCodeFirstClient(
        IClientAccessor<TClient> accessor,
        IRetryPolicy retryPolicy,
        IClock clock,
        TimeSpan operationTimeout)
    {
        _accessor = accessor;
        _retryPolicy = retryPolicy;
        _clock = clock;
        _operationTimeout = operationTimeout;

        LinkLifetime(accessor);
    }

    protected async Task<TResult> ExecuteAsync<TResult>(
        OperationContext originalContext,
        Func<OperationContext, CallOptions, TClient, Task<TResult>> executeAsync,
        Func<TResult, string?> extraEndMessage,
        string? extraStartMessage = null,
        [CallerMemberName] string caller = null!)
        where TResult : ResultBase
    {
        var attempt = -1;
        using var contextWithShutdown = TrackShutdown(originalContext);
        var context = contextWithShutdown.Context;
        var callerAttempt = $"{caller}_Attempt";

        return await context.PerformOperationWithTimeoutAsync(
            Tracer,
            context =>
            {
                var callOptions = new CallOptions(
                    headers: new Metadata() { MetadataServiceSerializer.CreateContextIdHeaderEntry(context.TracingContext.TraceId) },
                    deadline: _clock.UtcNow + _operationTimeout,
                    cancellationToken: context.Token);

                return _retryPolicy.ExecuteAsync(
                    async () =>
                    {
                        await Task.Yield();

                        attempt++;

                        var stopwatch = StopwatchSlim.Start();
                        var clientCreationTime = TimeSpan.Zero;

                        var result = await context.PerformOperationAsync(
                            Tracer,
                            () =>
                            {
                                return _accessor.UseAsync(
                                    context,
                                    service =>
                                    {
                                        clientCreationTime = stopwatch.Elapsed;

                                        return executeAsync(context, callOptions, service);
                                    });
                            },
                            extraStartMessage: extraStartMessage,
                            extraEndMessage:
                            r => $"Attempt=[{attempt}] ClientCreationTimeMs=[{clientCreationTime.TotalMilliseconds}] {extraEndMessage(r)}",
                            caller: callerAttempt,
                            traceErrorsOnly: true);

                        await Task.Yield();

                        // Because we capture exceptions inside the PerformOperation, we need to make sure that they
                        // get propagated for the retry policy to kick in.
                        result.RethrowIfFailure();

                        return result;
                    },
                    context.Token);
            },
            caller: caller,
            traceErrorsOnly: true,
            extraStartMessage: extraStartMessage,
            extraEndMessage: r => $"Attempts=[{attempt + 1}] {extraEndMessage(r)}",
            timeout: _operationTimeout);
    }
}
