using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using static BuildXL.Cache.Host.Configuration.DeploymentManifest;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Tracing;
using System.Threading;
using System.Text;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.FileSystem;

namespace BuildXL.Launcher.Server
{
    /// <summary>
    /// Service used ensure deployments are uploaded to target storage accounts and provide manifest for with download urls and tools to launch
    /// </summary>
    public class DeploymentProxyService : StartupShutdownSlimBase
    {
        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(DeploymentProxyService));

        /// <summary>
        /// The root of the mounted deployment folder created by the <see cref="DeploymentIngester"/>
        /// </summary>
        private AbsolutePath Root { get; }

        /// <summary>
        /// Map for getting expirable sas urls by storage account and hash 
        /// </summary>
        private VolatileMap<(string hash, string token), AsyncLazy<BoolResult>> ContentCacheRequests { get; }

        private VolatileMap<UnitValue, AsyncLazy<string>> ProxyAddress { get; }

        private IClock Clock { get; }

        private ActionQueue DownloadQueue { get; }

        private ProxyServiceConfiguration Configuration { get; }

        public FileSystemContentStoreInternal Store { get; }

        public IDeploymentServiceClient Client { get; }

        private HostParameters HostParameters { get; }

        /// <nodoc />
        public DeploymentProxyService(
            ProxyServiceConfiguration configuration,
            HostParameters hostParameters,
            IAbsFileSystem fileSystem = null,
            IClock clock = null,
            IDeploymentServiceClient client = null)
        {
            clock ??= SystemClock.Instance;
            Configuration = configuration;
            Root = new AbsolutePath(configuration.RootPath);
            Clock = clock;
            ContentCacheRequests = new VolatileMap<(string, string), AsyncLazy<BoolResult>>(Clock);
            ProxyAddress = new VolatileMap<UnitValue, AsyncLazy<string>>(Clock);
            Client = client ?? DeploymentLauncherHost.Instance.CreateServiceClient();
            HostParameters = hostParameters;

            DownloadQueue = new ActionQueue(configuration.DownloadConcurrency ?? Environment.ProcessorCount);

            Store = new FileSystemContentStoreInternal(
                fileSystem ?? new PassThroughFileSystem(),
                Clock,
                DeploymentUtilities.GetCasRootPath(Root),
                new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota($"{Configuration.RetentionSizeGb}GB"))),
                settings: new ContentStoreSettings()
                {
                    TraceFileSystemContentStoreDiagnosticMessages = true,
                });
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await Store.StartupAsync(context).ThrowIfFailureAsync();
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var success = await Store.ShutdownAsync(context);
            return success;
        }

        internal static string GetContentUrl(Context context, string baseAddress, string hash, string accessToken)
        {
            static string escape(string value) => Uri.EscapeDataString(value);

            return $"{baseAddress.TrimEnd('/')}/content?contextId={escape(context.TraceId)}&hash={escape(hash)}&accessToken={escape(accessToken)}";
        }

        public Task<Stream> GetContentAsync(OperationContext context, string hash, string accessToken)
        {
            long length = 0;
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var contentHash = new ContentHash(hash);
                    var openResult = await Store.OpenStreamAsync(context, contentHash, pinRequest: null);
                    if (openResult.Code == OpenStreamResult.ResultCode.ContentNotFound)
                    {
                        await EnsureContentLocalAsync(context, hash, accessToken);
                        openResult = await Store.OpenStreamAsync(context, contentHash, pinRequest: null).ThrowIfFailure();
                    }
                    else
                    {
                        openResult.ThrowIfFailure();
                    }

                    length = openResult.StreamWithLength?.Length ?? -1;
                    return Result.Success(openResult.Stream);
                },
                extraStartMessage: $"Hash={hash}",
                extraEndMessage: r => $"Hash={hash}, Size={length}"
                ).ThrowIfFailureAsync();
        }

        private Task<string> GetProxyBaseAddressAsync(OperationContext context, string token)
        {
            var cacheRequestTimeToLive = Configuration.ProxyAddressTimeToLive;
            AsyncLazy<string> lazyProxyAddress = GetOrAddExpirableAsyncLazy(
                ProxyAddress,
                UnitValue.Unit,
                cacheRequestTimeToLive,
                () =>
                {
                    return context.PerformOperationWithTimeoutAsync(
                        Tracer,
                        async innerContext =>
                        {
                            return await Client.GetProxyBaseAddress(innerContext, Configuration.DeploymentServiceUrl, HostParameters, token).AsSuccessAsync();
                        },
                        timeout: cacheRequestTimeToLive,
                        extraEndMessage: r => $"BaseAddress={r.GetValueOrDefault()}").ThrowIfFailureAsync();
                });

            return lazyProxyAddress.GetValueAsync();
        }

        /// <summary>
        /// Ensures the requested content is cached locally
        /// </summary>
        private async Task EnsureContentLocalAsync(OperationContext context, string hash, string token)
        {
            var proxyBaseAddress = await GetProxyBaseAddressAsync(context, token);

            var cacheRequestTimeToLive = Configuration.ProxyAddressTimeToLive;
            var key = (hash, token);
            AsyncLazy<BoolResult> lazyCacheRequest = GetOrAddExpirableAsyncLazy(
                ContentCacheRequests,
                key,
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
                                var url = GetContentUrl(innerContext, proxyBaseAddress, hash, token);
                                var stream = await Client.GetStreamAsync(innerContext, url);

                                // Cache the content in local store and return stream from local store
                                return await Store.PutStreamAsync(innerContext, stream, contentHash, pinRequest: null).ThrowIfFailure();
                            },
                            extraStartMessage: $"Hash={hash} BaseAddress={proxyBaseAddress}",
                            extraEndMessage: r => $"Hash={hash} Size={r.ContentSize} BaseAddress={proxyBaseAddress}",
                            timeout: cacheRequestTimeToLive);
                    });
                });

            await lazyCacheRequest.GetValueAsync().ThrowIfFailureAsync();
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
