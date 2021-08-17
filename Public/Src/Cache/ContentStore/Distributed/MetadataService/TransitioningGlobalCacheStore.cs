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

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class TransitioningGlobalCacheStore : StartupShutdownSlimBase, IGlobalCacheStore
    {
        private readonly IGlobalCacheStore _redisStore;
        private readonly IGlobalCacheStore _distributedStore;
        private readonly RedisContentLocationStoreConfiguration _configuration;

        public bool AreBlobsSupported { get; }

        public override bool AllowMultipleStartupAndShutdowns => true;

        private ContentMetadataStoreMode BlobMode => (_configuration.BlobContentMetadataStoreModeOverride ?? _configuration.ContentMetadataStoreMode).Mask(_blobSupportedMask);
        private ContentMetadataStoreMode LocationMode => _configuration.LocationContentMetadataStoreModeOverride ?? _configuration.ContentMetadataStoreMode;
        private ContentMetadataStoreMode MemoizationMode => _configuration.MemoizationContentMetadataStoreModeOverride ?? _configuration.ContentMetadataStoreMode;
        private ContentMetadataStoreMode ClusterMode => _configuration.ClusterGlobalStoreModeOverride ?? _configuration.ContentMetadataStoreMode;

        private readonly ContentMetadataStoreModeFlags _blobSupportedMask;

        protected override Tracer Tracer { get; } = new Tracer(nameof(TransitioningGlobalCacheStore));

        public TransitioningGlobalCacheStore(RedisContentLocationStoreConfiguration configuration, IGlobalCacheStore redisStore, IGlobalCacheStore distributedStore)
        {
            Contract.Requires(configuration.AllContentMetadataStoreModeFlags.HasAnyFlag(ContentMetadataStoreModeFlags.Redis)
                && configuration.AllContentMetadataStoreModeFlags.HasAnyFlag(ContentMetadataStoreModeFlags.Distributed),
                "Transitioning store should not used for cases where one store or the other is used exclusively");

            _configuration = configuration;
            _redisStore = redisStore;
            _distributedStore = distributedStore;
            
            if (BlobMode.HasAllFlags(ContentMetadataStoreModeFlags.PreferRedis) || BlobMode.MaskFlags(ContentMetadataStoreModeFlags.Distributed) == 0)
            {
                AreBlobsSupported = _redisStore.AreBlobsSupported;
            }
            else if (BlobMode.HasAllFlags(ContentMetadataStoreModeFlags.PreferDistributed) || BlobMode.MaskFlags(ContentMetadataStoreModeFlags.Redis) == 0)
            {
                AreBlobsSupported = _distributedStore.AreBlobsSupported;
            }
            else
            {
                AreBlobsSupported = _redisStore.AreBlobsSupported || _distributedStore.AreBlobsSupported;
            }

            // Mask used to only include valid flags for BlobMode based on blob support in the respective stores
            _blobSupportedMask = ContentMetadataStoreModeFlags.All
                .Subtract(_redisStore.AreBlobsSupported ? 0 : ContentMetadataStoreModeFlags.Redis | ContentMetadataStoreModeFlags.PreferRedis)
                .Subtract(_distributedStore.AreBlobsSupported ? 0 : ContentMetadataStoreModeFlags.Distributed | ContentMetadataStoreModeFlags.PreferDistributed);
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

        public Task<Result<HeartbeatMachineResponse>> HeartbeatAsync(OperationContext context, HeartbeatMachineRequest request)
        {
            return WriteAsync(context, ClusterMode, store => store.HeartbeatAsync(context, request));
        }

        public Task<Result<GetClusterUpdatesResponse>> GetClusterUpdatesAsync(OperationContext context, GetClusterUpdatesRequest request)
        {
            return ReadAsync(context, ClusterMode, store => store.GetClusterUpdatesAsync(context, request));
        }

        public Task<Result<IReadOnlyList<ContentLocationEntry>>> GetBulkAsync(OperationContext context, IReadOnlyList<ShortHash> contentHashes)
        {
            return ReadAsync(context, LocationMode, store => store.GetBulkAsync(context, contentHashes));
        }

        public Task<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> contentHashes, bool touch)
        {
            return WriteAsync(context, LocationMode, store => store.RegisterLocationAsync(context, machineId, contentHashes, touch));
        }

        public Task<PutBlobResult> PutBlobAsync(OperationContext context, ShortHash hash, byte[] blob)
        {
            return WriteAsync(context, BlobMode, store => store.PutBlobAsync(context, hash, blob));
        }

        public Task<GetBlobResult> GetBlobAsync(OperationContext context, ShortHash hash)
        {
            return ReadAsync(context, BlobMode, store => store.GetBlobAsync(context, hash));
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

        private async Task<TResult> ExecuteAsync<TResult>(
            OperationContext context,
            ContentMetadataStoreMode mode,
            ContentMetadataStoreModeFlags modeMask,
            Func<IGlobalCacheStore, Task<TResult>> executeAsync,
            string caller)
            where TResult : ResultBase
        {
            ContentMetadataStoreModeFlags redisFlags, distributedFlags;
            bool preferRedis, preferDistributed;
            GetFlagsAndPreferences(mode, modeMask, out redisFlags, out distributedFlags, out preferRedis, out preferDistributed);

            var redisTask = MeasuredExecuteAsync(_redisStore, redisFlags, executeAsync);
            var distributedTask = MeasuredExecuteAsync(_distributedStore, distributedFlags, executeAsync);

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

        private async ValueTask<(TResult result, string message)> MeasuredExecuteAsync<TResult>(
            IGlobalCacheStore store,
            ContentMetadataStoreModeFlags flags,
            Func<IGlobalCacheStore, Task<TResult>> executeAsync)
            where TResult : ResultBase
        {
            if (flags == 0)
            {
                return (default, "[0ms, None]");
            }

            // The Redis tasks have long synchronous parts
            await Task.Yield();
            var sw = StopwatchSlim.Start();
            var result = await executeAsync(store);
            var message = $"[{Math.Round(sw.Elapsed.TotalMilliseconds, 3)}ms, {result.GetStatus()}]";
            return (result, message);
        }

        private async Task<(T value1, T value2)> WhenBothAsync<T>(ValueTask<T> task1, ValueTask<T> task2)
        {
            return (await task1, await task2);
        }
    }
}
