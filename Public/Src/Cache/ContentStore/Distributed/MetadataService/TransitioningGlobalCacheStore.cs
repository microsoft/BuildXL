// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Tracing;
using BoolResult = BuildXL.Cache.ContentStore.Interfaces.Results.BoolResult;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class TransitioningGlobalCacheStore : StartupShutdownSlimBase, IGlobalCacheStore
    {
        private readonly IGlobalCacheStore _redisStore;
        private readonly IGlobalCacheStore _distributedStore;
        private readonly RedisContentLocationStoreConfiguration _configuration;

        public override bool AllowMultipleStartupAndShutdowns => true;

        private ContentMetadataStoreMode LocationMode => _configuration.LocationContentMetadataStoreModeOverride ?? _configuration.ContentMetadataStoreMode;
        private ContentMetadataStoreMode MemoizationMode => _configuration.MemoizationContentMetadataStoreModeOverride ?? _configuration.ContentMetadataStoreMode;

        protected override Tracer Tracer { get; } = new Tracer(nameof(TransitioningGlobalCacheStore));

        public TransitioningGlobalCacheStore(RedisContentLocationStoreConfiguration configuration, IGlobalCacheStore redisStore, IGlobalCacheStore distributedStore)
        {
            Contract.Requires(configuration.AllContentMetadataStoreModeFlags.HasAnyFlag(ContentMetadataStoreModeFlags.Redis)
                && configuration.AllContentMetadataStoreModeFlags.HasAnyFlag(ContentMetadataStoreModeFlags.Distributed),
                "Transitioning store should not used for cases where one store or the other is used exclusively");

            _configuration = configuration;
            _redisStore = redisStore;
            _distributedStore = distributedStore;
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var result = BoolResult.Success;
            result &= await _redisStore.StartupAsync(context);
            result &= await _distributedStore.StartupAsync(context);

            return result;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = BoolResult.Success;
            result &= await _redisStore.ShutdownAsync(context);
            result &= await _distributedStore.ShutdownAsync(context);

            return result;
        }

        public Task<Result<IReadOnlyList<ContentLocationEntry>>> GetBulkAsync(OperationContext context, IReadOnlyList<ShortHash> contentHashes)
        {
            return ReadAsync(context, LocationMode, store => store.GetBulkAsync(context, contentHashes));
        }

        public ValueTask<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> contentHashes, bool touch)
        {
            return new ValueTask<BoolResult>(
                WriteAsync(
                    context,
                    LocationMode,
                    (context, machineId, contentHashes, touch),
                    static (store, tpl) => store.RegisterLocationAsync(tpl.context, tpl.machineId, tpl.contentHashes, tpl.touch).AsTask()));
        }

        public Task<Result<bool>> CompareExchangeAsync(OperationContext context, StrongFingerprint strongFingerprint, SerializedMetadataEntry replacement, string expectedReplacementToken)
        {
            return WriteAsync(context, MemoizationMode, store => store.CompareExchangeAsync(context, strongFingerprint, replacement, expectedReplacementToken));
        }

        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            return ReadAsync(context, MemoizationMode, store => store.GetLevelSelectorsAsync(context, weakFingerprint, level));
        }

        public Task<Result<SerializedMetadataEntry>> GetContentHashListAsync(OperationContext context, StrongFingerprint strongFingerprint)
        {
            return ReadAsync(context, MemoizationMode, store => store.GetContentHashListAsync(context, strongFingerprint));
        }

        public Task<TResult> ReadAsync<TResult>(OperationContext context, ContentMetadataStoreMode mode, Func<IGlobalCacheStore, Task<TResult>> executeAsync, [CallerMemberName] string caller = null)
            where TResult : ResultBase
        {
            return ExecuteAsync(context, mode, ContentMetadataStoreModeFlags.ReadBoth, executeAsync, caller);
        }

        public Task<TResult> WriteAsync<TResult>(OperationContext context, ContentMetadataStoreMode mode, Func<IGlobalCacheStore, Task<TResult>> executeAsync, [CallerMemberName] string caller = null)
            where TResult : ResultBase
        {
            return ExecuteAsync(context, mode, ContentMetadataStoreModeFlags.WriteBoth, executeAsync, caller);
        }

        public Task<TResult> WriteAsync<TResult, TState>(OperationContext context, ContentMetadataStoreMode mode, TState state, Func<IGlobalCacheStore, TState, Task<TResult>> executeAsync, [CallerMemberName] string caller = null)
            where TResult : ResultBase
        {
            return ExecuteAsync(context, mode, ContentMetadataStoreModeFlags.WriteBoth, state, executeAsync, caller);
        }

        private Task<TResult> ExecuteAsync<TResult>(
            OperationContext context,
            ContentMetadataStoreMode mode,
            ContentMetadataStoreModeFlags modeMask,
            Func<IGlobalCacheStore, Task<TResult>> executeAsync,
            [CallerMemberName] string caller = null)
            where TResult : ResultBase
        {
            return ExecuteAsync(context, mode, modeMask, executeAsync, (store, execute) => execute(store), caller);
        }

        private async Task<TResult> ExecuteAsync<TResult, TState>(
            OperationContext context,
            ContentMetadataStoreMode mode,
            ContentMetadataStoreModeFlags modeMask,
            TState state,
            Func<IGlobalCacheStore, TState, Task<TResult>> executeAsync,
            string caller)
            where TResult : ResultBase
        {
            ContentMetadataStoreModeFlags redisFlags, distributedFlags;
            bool preferRedis, preferDistributed;
            GetFlagsAndPreferences(mode, modeMask, out redisFlags, out distributedFlags, out preferRedis, out preferDistributed);

            var redisTask = MeasuredExecuteAsync(_redisStore, redisFlags, state, executeAsync);
            var distributedTask = MeasuredExecuteAsync(_distributedStore, distributedFlags, state, executeAsync);

            string extraEndMessage = null;
            var combinedResultTask = context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var (redisResult, distributedResult) = await WhenBothAsync(redisTask, distributedTask);
                    var result = preferDistributed ? distributedResult : redisResult;
                    extraEndMessage = $"Redis={redisResult.message}, Distrib={distributedResult.message}";
                    return result.result;
                },
                caller: caller,
                extraEndMessage: r => extraEndMessage);

            if (preferRedis)
            {
                // If preferring redis, return the redis result without waiting for combined result
                return (await redisTask).result;
            }
            else if (preferDistributed)
            {
                // If preferring distributed, return the distributed result without waiting for combined result
                return (await distributedTask).result;
            }
            else
            {
                return await combinedResultTask;
            }
        }

        private static void GetFlagsAndPreferences(ContentMetadataStoreMode mode, ContentMetadataStoreModeFlags modeMask, out ContentMetadataStoreModeFlags redisFlags, out ContentMetadataStoreModeFlags distributedFlags, out bool preferRedis, out bool preferDistributed)
        {
            var modeFlags = mode.MaskFlags(modeMask);
            redisFlags = modeFlags & ContentMetadataStoreModeFlags.Redis;
            distributedFlags = modeFlags & ContentMetadataStoreModeFlags.Distributed;
            var preference = mode.MaskFlags(ContentMetadataStoreModeFlags.PreferenceMask);
            preferRedis = preference.HasAllFlags(ContentMetadataStoreModeFlags.PreferRedis) || distributedFlags == 0;
            preferDistributed = preference.HasAllFlags(ContentMetadataStoreModeFlags.PreferDistributed) || redisFlags == 0;
        }

        private async ValueTask<(TResult result, string message)> MeasuredExecuteAsync<TResult, TState>(
            IGlobalCacheStore store,
            ContentMetadataStoreModeFlags flags,
            TState state,
            Func<IGlobalCacheStore, TState, Task<TResult>> executeAsync)
            where TResult : ResultBase
        {
            if (flags == 0)
            {
                return (default, "[0ms, None]");
            }

            // The Redis tasks have long synchronous parts
            await Task.Yield();
            var sw = StopwatchSlim.Start();
            var result = await executeAsync(store, state);
            var message = $"[{Math.Round(sw.Elapsed.TotalMilliseconds, 3)}ms, {result.GetStatus()}]";
            return (result, message);
        }

        private async Task<(T value1, T value2)> WhenBothAsync<T>(ValueTask<T> task1, ValueTask<T> task2)
        {
            return (await task1, await task2);
        }
    }
}
