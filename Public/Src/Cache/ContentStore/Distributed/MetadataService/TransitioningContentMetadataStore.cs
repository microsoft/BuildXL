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
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class TransitioningContentMetadataStore : StartupShutdownSlimBase, IContentMetadataStore
    {
        private readonly IContentMetadataStore _redisStore;
        private readonly IContentMetadataStore _distributedStore;
        private readonly RedisContentLocationStoreConfiguration _configuration;
        private readonly bool _preferDistributed;
        private readonly bool _preferRedis;

        public bool AreBlobsSupported { get; }

        private readonly ContentMetadataStoreModeFlags _blobSupportedMask;

        protected override Tracer Tracer { get; } = new Tracer(nameof(TransitioningContentMetadataStore));

        public TransitioningContentMetadataStore(RedisContentLocationStoreConfiguration configuration, IContentMetadataStore redisStore, IContentMetadataStore distributedStore)
        {
            Contract.Requires(configuration.ContentMetadataStoreMode != ContentMetadataStoreMode.Redis
                && configuration.ContentMetadataStoreMode != ContentMetadataStoreMode.Distributed,
                "Transitioning store should not used for cases where one store or the other is used exclusively");

            _configuration = configuration;
            _redisStore = redisStore;
            _distributedStore = distributedStore;
            _preferDistributed = configuration.ContentMetadataStoreModeFlags.HasFlag(ContentMetadataStoreModeFlags.PreferDistributed);
            _preferRedis = configuration.ContentMetadataStoreModeFlags.HasFlag(ContentMetadataStoreModeFlags.PreferRedis);

            if (_preferRedis)
            {
                AreBlobsSupported = _redisStore.AreBlobsSupported;
            }
            else if (_preferDistributed)
            {
                AreBlobsSupported = _distributedStore.AreBlobsSupported;
            }
            else
            {
                AreBlobsSupported = _redisStore.AreBlobsSupported || _distributedStore.AreBlobsSupported;
            }

            _blobSupportedMask =
                (_redisStore.AreBlobsSupported ? ContentMetadataStoreModeFlags.Redis : 0)
                | (_distributedStore.AreBlobsSupported ? ContentMetadataStoreModeFlags.Distributed : 0);
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
            return ReadAsync(context, store => store.GetBulkAsync(context, contentHashes));
        }

        public Task<BoolResult> RegisterLocationAsync(OperationContext context, MachineId machineId, IReadOnlyList<ShortHashWithSize> contentHashes, bool touch)
        {
            return WriteAsync(context, store => store.RegisterLocationAsync(context, machineId, contentHashes, touch));
        }

        public Task<PutBlobResult> PutBlobAsync(OperationContext context, ShortHash hash, byte[] blob)
        {
            return WriteAsync(context, store => store.PutBlobAsync(context, hash, blob), modeMask: _blobSupportedMask);
        }

        public Task<GetBlobResult> GetBlobAsync(OperationContext context, ShortHash hash)
        {
            return ReadAsync(context, store => store.GetBlobAsync(context, hash), modeMask: _blobSupportedMask);
        }

        public Task<TResult> ReadAsync<TResult>(OperationContext context, Func<IContentMetadataStore, Task<TResult>> executeAsync, [CallerMemberName] string caller = null, ContentMetadataStoreModeFlags modeMask = ContentMetadataStoreModeFlags.All)
            where TResult : ResultBase
        {
            return ExecuteAsync(context, ContentMetadataStoreModeFlags.ReadBoth & modeMask, executeAsync, caller);
        }

        public Task<TResult> WriteAsync<TResult>(OperationContext context, Func<IContentMetadataStore, Task<TResult>> executeAsync, [CallerMemberName] string caller = null, ContentMetadataStoreModeFlags modeMask = ContentMetadataStoreModeFlags.All)
            where TResult : ResultBase
        {
            return ExecuteAsync(context, ContentMetadataStoreModeFlags.WriteBoth & modeMask, executeAsync, caller);
        }

        private async Task<TResult> ExecuteAsync<TResult>(
            OperationContext context,
            ContentMetadataStoreModeFlags modeMask,
            Func<IContentMetadataStore, Task<TResult>> executeAsync,
            string caller)
            where TResult : ResultBase
        {
            var mode = _configuration.ContentMetadataStoreModeFlags & modeMask;
            var preference = _configuration.ContentMetadataStoreModeFlags & ContentMetadataStoreModeFlags.PreferenceMask;
            var redisFlags = mode & ContentMetadataStoreModeFlags.Redis;
            var redisTask = MeasuredExecuteAsync(_redisStore, redisFlags, executeAsync);
            var distributedFlags = mode & ContentMetadataStoreModeFlags.Distributed;
            var distributedTask = MeasuredExecuteAsync(_distributedStore, distributedFlags, executeAsync);

            string extraEndMessage = null;
            var combinedResultTask = context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var (redisResult, distributedResult) = await WhenBothAsync(redisTask, distributedTask);
                    var result = _preferDistributed ? distributedResult : redisResult;
                    extraEndMessage = $"Redis={redisResult.message}, Distrib={distributedResult.message}";
                    return result.result;
                },
                caller: caller,
                extraEndMessage: r => extraEndMessage);

            if (_preferRedis || distributedFlags == 0)
            {
                // If preferring redis, return the redis result without waiting for combined result
                return (await redisTask).result;
            }
            else if (_preferDistributed || redisFlags == 0)
            {
                // If preferring distributed, return the distributed result without waiting for combined result
                return (await distributedTask).result;
            }
            else
            {
                return await combinedResultTask;
            }
        }

        private async ValueTask<(TResult result, string message)> MeasuredExecuteAsync<TResult>(
            IContentMetadataStore store,
            ContentMetadataStoreModeFlags flags,
            Func<IContentMetadataStore, Task<TResult>> executeAsync)
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
