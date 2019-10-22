// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using StackExchange.Redis;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
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
        public RedisDatabaseAdapter SecondaryRedisDb { get; }

        private Tracer Tracer { get; }

        public bool HasSecondary => SecondaryRedisDb != null;

        /// <nodoc />
        public RaidedRedisDatabase(Tracer tracer, RedisDatabaseAdapter primaryRedisDb, RedisDatabaseAdapter secondaryRedisDb)
        {
            Tracer = tracer;
            PrimaryRedisDb = primaryRedisDb;
            SecondaryRedisDb = secondaryRedisDb;
        }

        /// <nodoc />
        public CounterSet GetCounters(OperationContext context, Role? role, Counter counter)
        {
            var counters = new CounterSet();
            counters.Merge(PrimaryRedisDb.Counters.ToCounterSet(), "Redis.");

            if (role != Role.Worker)
            {
                // Don't print redis counters on workers
                counters.Merge(PrimaryRedisDb.GetRedisCounters(context, Tracer, counter), "RedisInfo.");
            }

            if (HasSecondary)
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
        public async Task<BoolResult> ExecuteRedisAsync(OperationContext context, Func<RedisDatabaseAdapter, Task<BoolResult>> executeAsync, [CallerMemberName]string caller = null)
        {
            (var primaryResult, var secondaryResult) = await ExecuteRaidedAsync(
                context,
                executeAsync,
                concurrent: true);

            if (!HasSecondary)
            {
                return primaryResult;
            }

            return primaryResult | secondaryResult;
        }

        /// <nodoc />
        public async Task<(TResult primary, TResult secondary)> ExecuteRaidedAsync<TResult>(OperationContext context, Func<RedisDatabaseAdapter, Task<TResult>> executeAsync, bool concurrent = true, [CallerMemberName]string caller = null)
            where TResult : BoolResult
        {
            var primaryResultTask = ExecuteAndCaptureRedisErrorsAsync(PrimaryRedisDb, executeAsync);

            if (!HasSecondary)
            {
                return (await primaryResultTask, default);
            }

            if (!concurrent)
            {
                // Wait for primary result before executing on secondary if concurrent execution on primary
                // and secondary is disabled
                await primaryResultTask.IgnoreErrorsAndReturnCompletion();
            }

            var secondaryResultTask = ExecuteAndCaptureRedisErrorsAsync(SecondaryRedisDb, executeAsync);
            await Task.WhenAll(primaryResultTask, secondaryResultTask);

            var primaryResult = await primaryResultTask;
            var secondaryResult = await secondaryResultTask;

            if (primaryResult.Succeeded != secondaryResult.Succeeded)
            {
                var failingRedisDb = GetDbName(primaryResult.Succeeded ? SecondaryRedisDb : PrimaryRedisDb);
                Tracer.Info(context, $"{Tracer.Name}.{caller}: Error in {failingRedisDb} redis db using result from other redis db: {primaryResult & secondaryResult}");
            }

            return (primaryResult, secondaryResult);
        }

        /// <nodoc />
        public async Task<TResult> ExecuteRedisFallbackAsync<TResult>(OperationContext context, Func<RedisDatabaseAdapter, Task<TResult>> executeAsync, [CallerMemberName]string caller = null)
            where TResult : ResultBase
        {
            var primaryResult = await ExecuteAndCaptureRedisErrorsAsync(PrimaryRedisDb, executeAsync);
            if (!primaryResult.Succeeded && HasSecondary)
            {
                Tracer.Info(context, $"{Tracer.Name}.{caller}: Error in {GetDbName(PrimaryRedisDb)} redis db falling back to secondary redis db: {primaryResult}");
                return await ExecuteAndCaptureRedisErrorsAsync(SecondaryRedisDb, executeAsync);
            }

            return primaryResult;
        }

        /// <nodoc />
        public string GetDbName(RedisDatabaseAdapter redisDb)
        {
            return redisDb == PrimaryRedisDb ? "primary" : "secondary";
        }

        /// <nodoc />
        public bool IsPrimary(RedisDatabaseAdapter redisDb)
        {
            return redisDb == PrimaryRedisDb;
        }

        /// <nodoc />
        public async Task<TResult> ExecuteAndCaptureRedisErrorsAsync<TResult>(RedisDatabaseAdapter redisDb, Func<RedisDatabaseAdapter, Task<TResult>> executeAsync)
            where TResult : ResultBase
        {
            try
            {
                return await executeAsync(redisDb);
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
