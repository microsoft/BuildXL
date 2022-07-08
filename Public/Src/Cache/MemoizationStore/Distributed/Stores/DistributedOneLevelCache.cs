// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Services;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Tracing;

#nullable enable annotations

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    /// Distributed OneLevelCache combining a content store and LocalLocationStore-backed DistributedMemoizationStore
    /// </summary>
    public class DistributedOneLevelCache : OneLevelCacheBase
    {
        /// <inheritdoc />
        protected override CacheTracer CacheTracer { get; } = new CacheTracer(nameof(DistributedOneLevelCache));

        private readonly DistributedContentStoreServices _services;

        /// <nodoc />
        public DistributedOneLevelCache(IContentStore contentStore, DistributedContentStoreServices services, Guid id, bool passContentToMemoization = true)
            : base(new OneLevelCacheBaseConfiguration(id, PassContentToMemoization: passContentToMemoization))
        {
            ContentStore = contentStore;
            _services = services;
        }

        /// <inheritdoc />
        protected override async Task<(BoolResult contentStoreResult, BoolResult memoizationStoreResult)> CreateAndStartStoresAsync(OperationContext context)
        {
            var contentStoreResult = await ContentStore.StartupAsync(context);
            if (!contentStoreResult)
            {
                return (contentStoreResult, new BoolResult("DistributedOneLevelCache memoization store requires successful content store startup"));
            }

            var memoizationStoreResult = await CreateDistributedMemoizationStoreAsync(context);

            return (contentStoreResult, memoizationStoreResult);
        }

        private Task<BoolResult> CreateDistributedMemoizationStoreAsync(OperationContext context)
        {
            return context.PerformOperationAsync(base.Tracer, async () =>
            {
                var services = _services;
                var locationStoreServices = _services.ContentLocationStoreServices.Instance;
                var localLocationStore = locationStoreServices.LocalLocationStore.Instance;
                var distributedSettings = services.Arguments.DistributedContentSettings;
                MemoizationDatabase getGlobalMemoizationDatabase()
                {
                    if (locationStoreServices.Configuration.UseMemoizationContentMetadataStore)
                    {
                        return new MetadataStoreMemoizationDatabase(
                            locationStoreServices.GlobalCacheStore.Instance,
                            locationStoreServices.Configuration.MetadataStoreMemoization,
                            locationStoreServices.CentralStorage.Instance);
                    }
                    else
                    {
                        var redisStore = locationStoreServices.RedisGlobalStore.Instance;
                        return new RedisMemoizationDatabase(redisStore.RaidedRedis, locationStoreServices.Configuration.Memoization);
                    }
                }

                MemoizationDatabase getLocalMemoizationDatabase()
                {
                    if (distributedSettings.UseGlobalCacheDatabaseInLocalLocationStore)
                    {
                        // Using GCS database locally. Need to get the wrapping
                        // RocksDbContentMetadataStore since GCS DB doesn't truly support all
                        // the metadata operations of the base type (ContentLocationDatabase)
                        var checkpointManager = services.GlobalCacheCheckpointManager;
                        return new MetadataStoreMemoizationDatabase(
                            services.RocksDbContentMetadataStore.Instance,
                            locationStoreServices.Configuration.MetadataStoreMemoization,
                            services.GlobalCacheStreamStorage.Instance);
                    }
                    else
                    {
                        return new RocksDbMemoizationDatabase(localLocationStore.Database, ownsDatabase: false);
                    }
                }

                MemoizationStore = new DatabaseMemoizationStore(
                    new DistributedMemoizationDatabase(
                        localDatabase: getLocalMemoizationDatabase(),
                        sharedDatabase: getGlobalMemoizationDatabase(),
                        localLocationStore))
                {
                    OptimizeWrites = distributedSettings.OptimizeDistributedCacheWrites == true,
                    RegisterAssociatedContent = distributedSettings.RegisterHintHandling.Value.HasFlag(RegisterHintHandling.RegisterAssociatedContent) == true
                };

                return await MemoizationStore.StartupAsync(context);
            });
        }
    }
}
