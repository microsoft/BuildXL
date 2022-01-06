// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Utilities;
using BuildXL.Utilities.ParallelAlgorithms;

namespace BuildXL.Launcher.Server
{
    /// <summary>
    /// Service used retrieved content from cache via an http endpoint
    /// </summary>
    public class ContentCacheService : StartupShutdownSlimBase
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(ContentCacheService));

        /// <summary>
        /// Map for getting expirable sas urls by storage account and hash 
        /// </summary>
        private VolatileMap<string, AsyncLazy<BoolResult>> ContentCacheRequests { get; }

        private IClock Clock { get; } = SystemClock.Instance;

        private ActionQueue DownloadQueue { get; }

        private IDeploymentServiceClient Client { get; }

        private IPushFileHandler PushFileHandler { get; }

        private IDistributedStreamStore StreamStore { get; }

        private ContentCacheConfiguration Configuration { get; }

        /// <nodoc />
        public ContentCacheService(
            ContentCacheConfiguration configuration,
            IPushFileHandler pushFileHandler,
            IDistributedStreamStore streamStore,
            IDeploymentServiceClient client = null)
        {
            Configuration = configuration;
            StreamStore = streamStore;
            PushFileHandler = pushFileHandler;
            ContentCacheRequests = new VolatileMap<string, AsyncLazy<BoolResult>>(Clock);
            Client = client ?? DeploymentLauncherHost.Instance.CreateServiceClient();

            DownloadQueue = new ActionQueue(configuration.DownloadConcurrency ?? Environment.ProcessorCount);
        }

        public Task<OpenStreamResult> GetContentAsync(OperationContext context, string hash, string downloadUrl  = null)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var contentHash = new ContentHash(hash);
                    var openResult = await StreamStore.OpenStreamAsync(context, contentHash);
                    if (openResult.Code == OpenStreamResult.ResultCode.ContentNotFound && downloadUrl != null)
                    {
                        await EnsureContentLocalAsync(context, hash, downloadUrl);
                        openResult = await StreamStore.OpenStreamAsync(context, contentHash);
                    }

                    return openResult;
                },
                extraStartMessage: $"Hash={hash}",
                extraEndMessage: r => $"Hash={hash}, Size={r.StreamWithLength?.Length ?? -1}"
                );
        }

        /// <summary>
        /// Ensures the requested content is cached locally
        /// </summary>
        private async Task EnsureContentLocalAsync(OperationContext context, string hash, string downloadUrl)
        {
            var cacheRequestTimeToLive = Configuration.DownloadTimeout;
            AsyncLazy<BoolResult> lazyCacheRequest = GetOrAddExpirableAsyncLazy(
                ContentCacheRequests,
                hash,
                cacheRequestTimeToLive,
                () =>
                {
                    return DownloadQueue.RunAsync<BoolResult>(async () =>
                    {
                        return await context.PerformOperationWithTimeoutAsync(
                            Tracer,
                            async innerContext =>
                            {
                                var contentHash = new ContentHash(hash);
                                var stream = await Client.GetStreamAsync(innerContext, downloadUrl);

                                // Cache the content in local store and return stream from local store
                                return await PushFileHandler.HandlePushFileAsync(
                                    innerContext,
                                    contentHash,
                                    new FileSource(stream),
                                    innerContext.Token).ThrowIfFailure();
                            },
                            extraStartMessage: $"Hash={hash}",
                            extraEndMessage: r => $"Hash={hash} Size={r.ContentSize}",
                            timeout: cacheRequestTimeToLive);
                    });
                });

            await lazyCacheRequest.GetValueAsync().ThrowIfFailureAsync();

            ContentCacheRequests.Invalidate(hash);
        }

        private AsyncLazy<TValue> GetOrAddExpirableAsyncLazy<TKey, TValue>(
            VolatileMap<TKey, AsyncLazy<TValue>> map,
            TKey key,
            TimeSpan timeToLive,
            Func<Task<TValue>> func)
        {
            AsyncLazy<TValue> asyncLazyValue;
            while (!map.TryGetValue(key, out asyncLazyValue))
            {
                bool invalidate()
                {
                    map.Invalidate(key);
                    return false;
                }

                asyncLazyValue = new AsyncLazy<TValue>(async () =>
                {
                    try
                    {
                        return await func();
                    }
                    catch (Exception) when (invalidate())
                    {
                        // This should never be reached.
                        // Using Exception filter to invalidate entry on exception
                        // and preserve stack trace
                        throw;
                    }
                });
                map.TryAdd(key, asyncLazyValue, timeToLive);
            }

            return asyncLazyValue;
        }
    }
}
