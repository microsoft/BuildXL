// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// A store that is based on content locations for opaque file locations.
    /// </summary>
    /// <typeparam name="T">The content locations being stored.</typeparam>
    public class DistributedContentStore<T> : StartupShutdownBase, IContentStore, IRepairStore, IDistributedLocationStore, IStreamStore
        where T : PathBase
    {
        private readonly byte[] _localMachineLocation;
        private readonly IContentLocationStoreFactory _contentLocationStoreFactory;
        private readonly ContentStoreTracer _tracer = new ContentStoreTracer(nameof(DistributedContentStore<T>));
        private readonly ReadOnlyDistributedContentSession<T>.ContentAvailabilityGuarantee _contentAvailabilityGuarantee;
        private readonly NagleQueue<ContentHash> _evictionNagleQueue;
        private NagleQueue<ContentHashWithSize> _touchNagleQueue;
        private readonly ContentTrackerUpdater _contentTrackerUpdater;
        private readonly bool _enableDistributedEviction;
        private readonly PinCache _pinCache;
        private readonly bool _enableRepairHandling;

        private readonly MachinePerformanceCollector _performanceCollector = new MachinePerformanceCollector();

        /// <summary>
        /// Flag for testing using local Redis instance.
        /// </summary>
        internal bool DisposeContentStoreFactory = true;

        internal IContentStore InnerContentStore { get; }

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        private IContentLocationStore _contentLocationStore;

        private readonly int _locationStoreBatchSize;

        private readonly DistributedContentStoreSettings _settings;

        private readonly TaskSourceSlim<BoolResult> _startupCompletion = TaskSourceSlim.Create<BoolResult>();

        private DistributedContentCopier<T> _distributedCopier;
        private readonly Func<IContentLocationStore, DistributedContentCopier<T>> _distributedCopierFactory;

        /// <nodoc />
        public DistributedContentStore(
            byte[] localMachineLocation,
            Func<NagleQueue<ContentHash>, DistributedEvictionSettings, ContentStoreSettings, TrimBulkAsync, IContentStore> innerContentStoreFunc,
            IContentLocationStoreFactory contentLocationStoreFactory,
            IFileExistenceChecker<T> fileExistenceChecker,
            IFileCopier<T> fileCopier,
            IPathTransformer<T> pathTransform,
            ReadOnlyDistributedContentSession<T>.ContentAvailabilityGuarantee contentAvailabilityGuarantee,
            AbsolutePath tempFolderForCopies,
            IAbsFileSystem fileSystem,
            int locationStoreBatchSize,
            IReadOnlyList<TimeSpan> retryIntervalForCopies = null,
            PinConfiguration pinConfiguration = null,
            int? replicaCreditInMinutes = null,
            IClock clock = null,
            bool enableRepairHandling = false,
            TimeSpan? contentHashBumpTime = null,
            bool useTrustedHash = false,
            int trustedHashFileSizeBoundary = -1,
            long parallelHashingFileSizeBoundary = -1,
            int maxConcurrentCopyOperations = 512,
            ContentStoreSettings contentStoreSettings = null)
            : this (
                  localMachineLocation,
                  innerContentStoreFunc,
                  contentLocationStoreFactory,
                  fileExistenceChecker,
                  fileCopier,
                  pathTransform,
                  contentAvailabilityGuarantee,
                  tempFolderForCopies,
                  fileSystem,
                  locationStoreBatchSize,
                  new DistributedContentStoreSettings()
                  {
                      UseTrustedHash = useTrustedHash,
                      TrustedHashFileSizeBoundary = trustedHashFileSizeBoundary,
                      ParallelHashingFileSizeBoundary = parallelHashingFileSizeBoundary,
                      MaxConcurrentCopyOperations = maxConcurrentCopyOperations,
                      RetryIntervalForCopies = retryIntervalForCopies,
                      PinConfiguration = pinConfiguration,
                  },
                  replicaCreditInMinutes,
                  clock,
                  enableRepairHandling,
                  contentHashBumpTime,
                  contentStoreSettings)
        {
        }

        /// <nodoc />
        public DistributedContentStore(
            byte[] localMachineLocation,
            Func<NagleQueue<ContentHash>, DistributedEvictionSettings, ContentStoreSettings, TrimBulkAsync, IContentStore> innerContentStoreFunc,
            IContentLocationStoreFactory contentLocationStoreFactory,
            IFileExistenceChecker<T> fileExistenceChecker,
            IFileCopier<T> fileCopier,
            IPathTransformer<T> pathTransform,
            ReadOnlyDistributedContentSession<T>.ContentAvailabilityGuarantee contentAvailabilityGuarantee,
            AbsolutePath tempFolderForCopies,
            IAbsFileSystem fileSystem,
            int locationStoreBatchSize,
            DistributedContentStoreSettings settings,
            int? replicaCreditInMinutes = null,
            IClock clock = null,
            bool enableRepairHandling = false,
            TimeSpan? contentHashBumpTime = null,
            ContentStoreSettings contentStoreSettings = null)
        {
            Contract.Requires(settings != null);

            _localMachineLocation = localMachineLocation;
            _enableRepairHandling = enableRepairHandling;
            _contentLocationStoreFactory = contentLocationStoreFactory;
            _contentAvailabilityGuarantee = contentAvailabilityGuarantee;
            _locationStoreBatchSize = locationStoreBatchSize;

            contentStoreSettings = contentStoreSettings ?? ContentStoreSettings.DefaultSettings;
            _settings = settings;

            // Queue is created in unstarted state because the eviction function
            // requires the context passed at startup.
            _evictionNagleQueue = NagleQueue<ContentHash>.CreateUnstarted(
                Redis.RedisContentLocationStoreConstants.BatchDegreeOfParallelism,
                Redis.RedisContentLocationStoreConstants.BatchInterval,
                _locationStoreBatchSize);

            _distributedCopierFactory = (contentLocationStore) =>
            {
                return new DistributedContentCopier<T>(
                    tempFolderForCopies,
                    _settings,
                    fileSystem,
                    fileCopier,
                    fileExistenceChecker,
                    pathTransform,
                    contentLocationStore);
            };

            _enableDistributedEviction = replicaCreditInMinutes != null;
            var distributedEvictionSettings = _enableDistributedEviction ? SetUpDistributedEviction(replicaCreditInMinutes, locationStoreBatchSize) : null;

            var enableTouch = contentHashBumpTime.HasValue;
            if (enableTouch)
            {
                _contentTrackerUpdater = new ContentTrackerUpdater(ScheduleBulkTouch, contentHashBumpTime.Value, clock: clock);
            }

            TrimBulkAsync trimBulkAsync = null;
            InnerContentStore = innerContentStoreFunc(_evictionNagleQueue, distributedEvictionSettings, contentStoreSettings, trimBulkAsync);

            if (settings.PinConfiguration?.UsePinCache == true)
            {
                _pinCache = new PinCache(clock: clock);
            }
        }

        /// <inheritdoc />
        public override Task<BoolResult> StartupAsync(Context context)
        {
            var startupTask = base.StartupAsync(context);
            _startupCompletion.LinkToTask(startupTask);
            return startupTask;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            // NOTE: We create and start the content location store before the inner content store just in case the
            // inner content store starts background eviction after startup. We need the content store to be initialized
            // so that it can be queried and used to unregister content.
            await _contentLocationStoreFactory.StartupAsync(context).ThrowIfFailure();

            _contentLocationStore = await _contentLocationStoreFactory.CreateAsync();

            _distributedCopier = _distributedCopierFactory(_contentLocationStore);
            await _distributedCopier.StartupAsync(context).ThrowIfFailure();

            if (_contentLocationStore is TransitioningContentLocationStore tcs)
            {
                tcs.LocalLocationStore.PreStartupInitialize(context, InnerContentStore as ILocalContentStore, _distributedCopier);
            }

            // Initializing inner store before initializing LocalLocationStore because
            // LocalLocationStore may use inner store for reconciliation purposes
            await InnerContentStore.StartupAsync(context).ThrowIfFailure();

            await _contentLocationStore.StartupAsync(context).ThrowIfFailure();

            Func<ContentHash[], Task> evictionHandler;
            var localContext = new Context(context);
            if (_enableDistributedEviction)
            {
                evictionHandler = hashes => EvictContentAsync(localContext, hashes);
            }
            else
            {
                evictionHandler = hashes => DistributedGarbageCollectionAsync(localContext, hashes);
            }

            // Queue is created in unstarted state because the eviction function
            // requires the context passed at startup. So we start the queue here.
            _evictionNagleQueue.Start(evictionHandler);

            var touchContext = new Context(context);
            _touchNagleQueue = NagleQueue<ContentHashWithSize>.Create(
                hashes => TouchBulkAsync(touchContext, hashes),
                Redis.RedisContentLocationStoreConstants.BatchDegreeOfParallelism,
                Redis.RedisContentLocationStoreConstants.BatchInterval,
                batchSize: _locationStoreBatchSize);

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var results = new List<Tuple<string, BoolResult>>();

            var innerResult = await InnerContentStore.ShutdownAsync(context);
            results.Add(Tuple.Create(nameof(InnerContentStore), innerResult));

            _evictionNagleQueue?.Dispose();
            _touchNagleQueue?.Dispose();

            var locationStoreResult = await _contentLocationStore.ShutdownAsync(context);
            results.Add(Tuple.Create(nameof(_contentLocationStore), locationStoreResult));

            var factoryResult = await _contentLocationStoreFactory.ShutdownAsync(context);
            results.Add(Tuple.Create(nameof(_contentLocationStoreFactory), factoryResult));

            if (_distributedCopier != null)
            {
                var copierResult = await _distributedCopier.ShutdownAsync(context);
                results.Add(Tuple.Create(nameof(_distributedCopier), copierResult));
            }

            return ShutdownErrorCompiler(results);
        }

        private void ScheduleBulkTouch(List<ContentHashWithSize> content)
        {
            Contract.Assert(_touchNagleQueue != null);
            _touchNagleQueue.EnqueueAll(content);
        }

        /// <summary>
        /// Batch content hashes that were not removed during eviction to re-register with the content tracker.
        /// </summary>
        private async Task EvictContentAsync(Context context, ContentHash[] contentHashes)
        {
            var contentHashesAndLocations = new List<ContentHashWithSizeAndLocations>();
            foreach (ContentHash contentHash in contentHashes)
            {
                _tracer.Debug(context, $"[DistributedEviction] Re-adding local location for content hash {contentHash} because it was not evicted");
                contentHashesAndLocations.Add(new ContentHashWithSizeAndLocations(contentHash));
            }

            // LocationStoreOption.None tells the content tracker to:
            //      1) Only update the location if the hash exists
            //      2) Not update the expiry
            var result = await _contentLocationStore.UpdateBulkAsync(
                context, contentHashesAndLocations, CancellationToken.None, UrgencyHint.Low, LocationStoreOption.None);

            if (!result.Succeeded)
            {
                _tracer.Error(context, $"[DistributedEviction] Unable to re-add content hashes to Redis. errorMessage=[{result.ErrorMessage}] diagnostics=[{result.Diagnostics}]");
            }
        }

        private async Task DistributedGarbageCollectionAsync(Context context, ContentHash[] contentHashes)
        {
            var result = await UnregisterAsync(context, contentHashes, CancellationToken.None);
            if (!result.Succeeded)
            {
                _tracer.Error(context, $"[GarbageCollection] Unable to remove evicted content hashes from Redis. errorMessage=[{result.ErrorMessage}] diagnostics=[{result.Diagnostics}]");
            }
        }

        private async Task TouchBulkAsync(Context context, ContentHashWithSize[] contentHashesWithSize)
        {
            var result = await _contentLocationStore.TouchBulkAsync(context, contentHashesWithSize, CancellationToken.None, UrgencyHint.Low);
            if (!result.Succeeded)
            {
                _tracer.Error(context, $"Unable to touch {contentHashesWithSize.Length} hashes in the content tracker. errorMessage=[{result.ErrorMessage}] diagnostics=[{result.Diagnostics}]");
            }
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(_tracer, OperationContext(context), name, () =>
            {
                CreateSessionResult<IContentSession> innerSessionResult = InnerContentStore.CreateSession(context, name, implicitPin);

                if (innerSessionResult.Succeeded)
                {
                    var session = new ReadOnlyDistributedContentSession<T>(
                            name,
                            innerSessionResult.Session,
                            _contentLocationStore,
                            _contentAvailabilityGuarantee,
                            _distributedCopier,
                            _localMachineLocation,
                            pinCache: _pinCache,
                            contentTrackerUpdater: _contentTrackerUpdater,
                            settings: _settings);
                    return new CreateSessionResult<IReadOnlyContentSession>(session);
                }

            return new CreateSessionResult<IReadOnlyContentSession>(innerSessionResult, "Could not initialize inner content session with error");
            });
        }

        /// <inheritdoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(_tracer, OperationContext(context), name, () =>
            {
                CreateSessionResult<IContentSession> innerSessionResult = InnerContentStore.CreateSession(context, name, implicitPin);

                if (innerSessionResult.Succeeded)
                {
                    var session = new DistributedContentSession<T>(
                            name,
                            innerSessionResult.Session,
                            _contentLocationStore,
                            _contentAvailabilityGuarantee,
                            _distributedCopier,
                            _localMachineLocation,
                            pinCache: _pinCache,
                            contentTrackerUpdater: _contentTrackerUpdater,
                            settings: _settings);
                    return new CreateSessionResult<IContentSession>(session);
                }

                return new CreateSessionResult<IContentSession>(innerSessionResult, "Could not initialize inner content session with error");
            });
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<ContentStoreTracer>.RunAsync(_tracer, OperationContext(context), async () =>
            {
                var result = await InnerContentStore.GetStatsAsync(context);
                if (result.Succeeded)
                {
                    var counterSet = result.CounterSet;
                    if (_contentLocationStore != null)
                    {
                        var contentLocationStoreCounters = _contentLocationStore.GetCounters(context);
                        counterSet.Merge(contentLocationStoreCounters, "ContentLocationStore.");
                    }

                    if (_pinCache != null)
                    {
                        counterSet.Merge(_pinCache.GetCounters(context), "PinCache.");
                    }

                    counterSet.Merge(_performanceCollector.GetPerformanceStats(), $"MachinePerf.");

                    return new GetStatsResult(counterSet);
                }

                return result;
            });
        }

        /// <summary>
        /// Remove local location from the content tracker.
        /// </summary>
        public async Task<StructResult<long>> RemoveFromTrackerAsync(Context context)
        {
            if (_enableRepairHandling && InnerContentStore is ILocalContentStore localStore)
            {
                var result = await _contentLocationStore.InvalidateLocalMachineAsync(context, localStore, CancellationToken.None);
                if (!result)
                {
                    return new StructResult<long>(result);
                }
            }

            // New logic doesn't have the content removed count
            return StructResult.Create((long)0);
        }

        /// <summary>
        /// Determines if final BoolResult is success or error.
        /// </summary>
        /// <param name="results">Paired List of shutdowns and their results.</param>
        /// <returns>BoolResult as success or error. If error, error message lists messages in order they occurred.</returns>
        private static BoolResult ShutdownErrorCompiler(IReadOnlyList<Tuple<string, BoolResult>> results)
        {
            var sb = new StringBuilder();
            foreach (Tuple<string, BoolResult> result in results)
            {
                if (!result.Item2.Succeeded)
                {
                    // TODO: Consider compiling Item2's Diagnostics into the final result's Diagnostics instead of ErrorMessage (bug 1365340)
                    sb.Append(result.Item1 + ": " + result.Item2 + " ");
                }
            }

            return sb.Length != 0 ? new BoolResult(sb.ToString()) : BoolResult.Success;
        }

        /// <nodoc />
        protected override void DisposeCore()
        {
            InnerContentStore.Dispose();

            if (DisposeContentStoreFactory)
            {
                _contentLocationStoreFactory.Dispose();
            }
        }

        private DistributedEvictionSettings SetUpDistributedEviction(int? replicaCreditInMinutes, int locationStoreBatchSize)
        {
            Contract.Assert(_enableDistributedEviction);

            return new DistributedEvictionSettings(
                (context, contentHashesWithInfo, cts, urgencyHint) =>
                    _contentLocationStore.TrimOrGetLastAccessTimeAsync(context, contentHashesWithInfo, cts, urgencyHint),
                locationStoreBatchSize,
                replicaCreditInMinutes,
                this);
        }

        /// <nodoc />
        public bool CanComputeLru => (_contentLocationStore as IDistributedLocationStore)?.CanComputeLru ?? false;

        /// <nodoc />
        public Task<BoolResult> UnregisterAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken token)
        {
            if (InnerContentStore is ILocalContentStore localContentStore)
            {
                // Filter out hashes which exist in the local content store (may have been re-added by a recent put).
                var filteredHashes = contentHashes.Where(hash => !localContentStore.Contains(hash)).ToList();
                if (filteredHashes.Count != contentHashes.Count)
                {
                    Tracer.OperationDebug(context, $"Hashes not unregistered because they are still present in local store: [{string.Join(",", contentHashes.Except(filteredHashes))}]");
                    contentHashes = filteredHashes;
                }
            }

            return _contentLocationStore.TrimBulkAsync(context, contentHashes, token, UrgencyHint.Nominal);
        }

        /// <nodoc />
        public IEnumerable<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>> GetLruPages(Context context, IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo)
        {
            // Ensure startup was called then wait for it to complete successfully (or error)
            // This logic is important to avoid runtime errors when, for instance, QuotaKeeper tries
            // to evict content right after startup and calls GetLruPages.
            Contract.Assert(StartupStarted);
            _startupCompletion.Task.GetAwaiter().GetResult().ThrowIfFailure();

            Contract.Assert(_contentLocationStore is IDistributedLocationStore);
            if (_contentLocationStore is IDistributedLocationStore distributedStore)
            {
                return distributedStore.GetLruPages(context, contentHashesWithInfo);
            }
            else
            {
                throw Contract.AssertFailure($"Cannot call GetLruPages when CanComputeLru returns false");
            }
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            if (InnerContentStore is IStreamStore innerStreamStore)
            {
                return await innerStreamStore.StreamContentAsync(context, contentHash);
            }

            return new OpenStreamResult($"{InnerContentStore} does not implement {nameof(IStreamStore)} in {nameof(DistributedContentStore<T>)}.");
        }

        /// <inheritdoc />
        public async Task<FileExistenceResult> CheckFileExistsAsync(Context context, ContentHash contentHash)
        {
            if (InnerContentStore is IStreamStore innerStreamStore)
            {
                return await innerStreamStore.CheckFileExistsAsync(context, contentHash);
            }

            return new FileExistenceResult(FileExistenceResult.ResultCode.Error, $"{InnerContentStore} does not implement {nameof(IStreamStore)} in {nameof(DistributedContentStore<T>)}.");
        }
    }
}
