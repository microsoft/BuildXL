// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Set of extension methods for <see cref="RedisBatch"/> class.
    /// </summary>
    internal static class RedisDatabaseAdapterExtensions
    {
        /// <summary>
        /// Executes a batch with a set of operations defined by <paramref name="addOperations"/> callback.
        /// </summary>
        public static async Task<T> ExecuteBatchAsync<T>(this RedisDatabaseAdapter adapter, OperationContext context, Func<RedisBatch, Task<T>> addOperations, RedisOperation operation)
        {
            var batch = adapter.CreateBatchOperation(operation);
            var result = addOperations((RedisBatch)batch);
            await adapter.ExecuteBatchOperationAsync(context, batch, context.Token).IgnoreFailure();
            return await result;
        }

        /// <summary>
        /// Gets the status of a redis instance.
        /// </summary>
        public static Task<Result<RedisInfoStats>> GetRedisInfoAsync(this RedisDatabaseAdapter adapter, OperationContext context, Tracer tracer, Counter counter, string serverId = null, bool trace = true)
        {
            return context.PerformOperationAsync(
                tracer,
                operation: async () =>
                {
                    var info = await adapter.GetInfoAsync(serverId);
                    return new Result<RedisInfoStats>(new RedisInfoStats(info));
                },
                traceOperationStarted: false,
                extraEndMessage: (result) => result.Succeeded ? $"Redis info: {string.Join(Environment.NewLine, result.Value.Info.Select(t => $"ServerId={t.serverId}, Info={t.info.ToString()}"))}" : null,
                counter: counter);
        }

        public static CounterSet GetRedisCounters(this RedisDatabaseAdapter adapter, OperationContext context, Tracer tracer, Counter counter)
        {
            var redisInfo = adapter.GetRedisInfoAsync(context, tracer, counter, serverId: null, trace: false).GetAwaiter().GetResult();

            var counterSet = new CounterSet();
            if (!redisInfo)
            {
                // The error is already logged.
                return counterSet;
            }

            foreach ((var serverId, var info) in redisInfo.Value.Info)
            {
                AddCounterValue($"{serverId}.{nameof(info.Uptime)}", (long?)info.Uptime?.TotalSeconds);
                AddCounterValue($"{serverId}.{nameof(info.KeyCount)}", info.KeyCount);
                AddCounterValue($"{serverId}.{nameof(info.ExpirableKeyCount)}", info.ExpirableKeyCount);
                AddCounterValue($"{serverId}.{nameof(info.EvictedKeys)}", info.EvictedKeys);
                AddCounterValue($"{serverId}.{nameof(info.KeySpaceHits)}", info.KeySpaceHits);
                AddCounterValue($"{serverId}.{nameof(info.KeySpaceMisses)}", info.KeySpaceMisses);
                AddCounterValue($"{serverId}.{nameof(info.UsedCpuAveragePersentage)}", info.UsedCpuAveragePersentage);
                AddCounterValue($"{serverId}.{nameof(info.UsedMemory)}Mb", (long?)(info.UsedMemory / Math.Pow(2, 20)));
                AddCounterValue($"{serverId}.{nameof(info.UsedMemoryRss)}Mb", (long?)(info.UsedMemoryRss / Math.Pow(2, 20)));
            }

            return counterSet;

            void AddCounterValue(string name, long? value)
            {
                if (value != null)
                {
                    counterSet.Add(name, value.Value);
                }
            }
        }
    }
}
