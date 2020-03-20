// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using StackExchange.Redis;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Enumeration for tracking performance of raided redis operations.
    /// </summary>
    public enum RaidedRedisDatabaseCounters
    {
        /// <summary>
        /// Counter for when we cancel one of the redis instance tasks because it takes too long
        /// </summary>
        CancelRedisInstance
    }

    internal sealed class RaidedRedisDatabase
    {
        /// <summary>
        /// Primary redis instance used for cluster state and locations
        /// </summary>
        public RedisDatabaseAdapter PrimaryRedisDb { get; }

        /// <summary>
        /// Secondary redis instance used to store backup of locations. NOT used for cluster state because
        /// reconciling these two is non-trivial and data loss does not typically occur with cluster state.
        /// </summary>
        public RedisDatabaseAdapter? SecondaryRedisDb { get; }

        private Tracer Tracer { get; }

        public bool HasSecondary => SecondaryRedisDb != null;

        /// <summary>
        /// Counters for tracking raided redis related operations
        /// </summary>
        public CounterCollection<RaidedRedisDatabaseCounters> Counters { get; } = new CounterCollection<RaidedRedisDatabaseCounters>();

        /// <nodoc />
        public RaidedRedisDatabase(Tracer tracer, RedisDatabaseAdapter primaryRedisDb, RedisDatabaseAdapter? secondaryRedisDb)
        {
            Tracer = tracer;
            PrimaryRedisDb = primaryRedisDb;
            SecondaryRedisDb = secondaryRedisDb;
        }

        /// <nodoc />
        public CounterSet GetCounters(OperationContext context, Role? role, Counter counter)
        {
            var counters = new CounterSet();
            counters.Merge(Counters.ToCounterSet(), "RaidedRedis.");
            counters.Merge(PrimaryRedisDb.Counters.ToCounterSet(), "Redis.");

            if (role != Role.Worker)
            {
                // Don't print redis counters on workers
                counters.Merge(PrimaryRedisDb.GetRedisCounters(context, Tracer, counter), "RedisInfo.");
            }

            if (SecondaryRedisDb != null)
            {
                counters.Merge(SecondaryRedisDb.Counters.ToCounterSet(), "SecondaryRedis.");

                if (role != Role.Worker)
                {
                    // Don't print redis counters on workers
                    counters.Merge(SecondaryRedisDb.GetRedisCounters(context, Tracer, counter), "SecondaryRedisInfo.");
                }
            }

            return counters;
        }

        /// <nodoc />
        public async Task<BoolResult> ExecuteRedisAsync(OperationContext context, Func<RedisDatabaseAdapter, CancellationToken, Task<BoolResult>> executeAsync, TimeSpan? retryWindow, [CallerMemberName]string? caller = null)
        {
            (var primaryResult, var secondaryResult) = await ExecuteRaidedAsync(
                context,
                executeAsync,
                retryWindow,
                concurrent: true);

            if (!HasSecondary)
            {
                return primaryResult!;
            }

            return primaryResult | secondaryResult;
        }

        /// <summary>
        /// Execute a given function against the primary and the secondary redis instances.
        /// </summary>
        /// <remarks>
        /// One of the elements in the result are not null.
        /// </remarks>
        public async Task<(TResult? primary, TResult? secondary)> ExecuteRaidedAsync<TResult>(OperationContext context, Func<RedisDatabaseAdapter, CancellationToken, Task<TResult>> executeAsync, TimeSpan? retryWindow, bool concurrent = true, [CallerMemberName]string? caller = null)
            where TResult : BoolResult
        {
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
            var primaryResultTask = ExecuteAndCaptureRedisErrorsAsync(PrimaryRedisDb, executeAsync, cancellationTokenSource.Token);

            if (SecondaryRedisDb == null)
            {
                return (await primaryResultTask, default);
            }

            if (!concurrent)
            {
                // Wait for primary result before executing on secondary if concurrent execution on primary
                // and secondary is disabled
                await primaryResultTask.IgnoreErrorsAndReturnCompletion();
            }

            var secondaryResultTask = ExecuteAndCaptureRedisErrorsAsync(SecondaryRedisDb, executeAsync, cancellationTokenSource.Token);

            // Instead of waiting for both - the primary and the secondary, we'll check for the primary first and then try to cancel the other one.
            // There is a time out delay acting as a window for the slower task to complete before we cancel the retry attempts

            Task<TResult> fasterResultTask = await Task.WhenAny(primaryResultTask, secondaryResultTask);
            Task<TResult> slowerResultTask = fasterResultTask == primaryResultTask ? secondaryResultTask : primaryResultTask;

            // Try to cancel the slower operation only when the faster one finished successfully (and the timeout was provided).
            if (fasterResultTask.Result.Succeeded && retryWindow != null)
            {
                // Giving the second task a chance to succeed.
                Task secondResult = await Task.WhenAny(slowerResultTask, Task.Delay(retryWindow.Value, context.Token));
                if (secondResult != slowerResultTask)
                {
                    var failingRedisDb = GetDbName(fasterResultTask == primaryResultTask ? SecondaryRedisDb : PrimaryRedisDb);
                    Counters[RaidedRedisDatabaseCounters.CancelRedisInstance].Increment();

                    // Avoiding task unobserved exception if the second task will fail.
                    slowerResultTask.FireAndForget(context, failureSeverity: Severity.Info, tracePrefix: $"{failingRedisDb}");

                    // The second task is not done within a given timeout.
                    cancellationTokenSource.Cancel();

                    Tracer.Info(context, $"{Tracer.Name}.{caller}: Cancelling redis db: {failingRedisDb}, using result: {fasterResultTask.Result} from other redis db");

                    if (fasterResultTask == primaryResultTask)
                    {
                        return (await fasterResultTask, default);
                    }
                    else
                    {
                        return (default, await fasterResultTask);
                    }
                }
            }

            await slowerResultTask;
            var primaryResult = await primaryResultTask;
            var secondaryResult = await secondaryResultTask;

            if (primaryResult.Succeeded != secondaryResult.Succeeded)
            {
                var failingRedisDb = GetDbName(primaryResult.Succeeded ? SecondaryRedisDb : PrimaryRedisDb);
                Tracer.Info(context, $"{Tracer.Name}.{caller}: Error in {failingRedisDb} redis db using result from other redis db: {(primaryResult.Succeeded ? primaryResult : secondaryResult)}");
            }

            return (primaryResult, secondaryResult);
        }

        /// <nodoc />
        public async Task<TResult> ExecuteRedisFallbackAsync<TResult>(OperationContext context, Func<RedisDatabaseAdapter, CancellationToken, Task<TResult>> executeAsync, [CallerMemberName]string? caller = null)
            where TResult : ResultBase
        {
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(context.Token);
            var primaryResult = await ExecuteAndCaptureRedisErrorsAsync(PrimaryRedisDb, executeAsync, cancellationTokenSource.Token);
            if (!primaryResult.Succeeded && SecondaryRedisDb != null)
            {
                Tracer.Info(context, $"{Tracer.Name}.{caller}: Error in {GetDbName(PrimaryRedisDb)} redis db falling back to secondary redis db: {primaryResult}");
                return await ExecuteAndCaptureRedisErrorsAsync(SecondaryRedisDb, executeAsync, cancellationTokenSource.Token);
            }

            return primaryResult;
        }

        /// <nodoc />
        public string GetDbName(RedisDatabaseAdapter redisDb)
        {
            return redisDb.DatabaseName;
        }

        /// <nodoc />
        public bool IsPrimary(RedisDatabaseAdapter redisDb)
        {
            return redisDb == PrimaryRedisDb;
        }

        /// <nodoc />
        public async Task<TResult> ExecuteAndCaptureRedisErrorsAsync<TResult>(RedisDatabaseAdapter redisDb, Func<RedisDatabaseAdapter, CancellationToken, Task<TResult>> executeAsync, CancellationToken token)
            where TResult : ResultBase
        {
            try
            {
                //executeAsync may have a long synchronous part
                await Task.Yield();
                return await executeAsync(redisDb, token);
            }
            catch (RedisConnectionException ex)
            {
                return new ErrorResult(ex).AsResult<TResult>();
            }
            catch (ResultPropagationException ex)
            {
                return new ErrorResult(ex).AsResult<TResult>();
            }
        }
    }
}
