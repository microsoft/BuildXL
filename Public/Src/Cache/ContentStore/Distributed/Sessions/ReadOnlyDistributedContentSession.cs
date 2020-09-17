// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using PlaceBulkResult = System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<BuildXL.Cache.ContentStore.Interfaces.Results.Indexed<BuildXL.Cache.ContentStore.Interfaces.Results.PlaceFileResult>>>;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// A read only content location based content session with an inner session for storage.
    /// </summary>
    /// <typeparam name="T">The content locations being stored.</typeparam>
    public class ReadOnlyDistributedContentSession<T> : ContentSessionBase, IHibernateContentSession, IConfigurablePin
        where T : PathBase
    {
        internal enum Counters
        {
            GetLocationsSatisfiedFromLocal,
            GetLocationsSatisfiedFromRemote,
            PinUnverifiedCountSatisfied,
            StartCopyForPinWhenUnverifiedCountSatisfied,
            ProactiveCopiesSkipped,
            ProactiveCopy_OutsideRingFromPreferredLocations,
            ProactiveCopyRetries,
            ProactiveCopy_InsideRingCopies,
            ProactiveCopy_InsideRingFullyReplicated,
        }

        internal CounterCollection<Counters> SessionCounters { get; } = new CounterCollection<Counters>();

        private string? _buildId = null;
        private ContentHash? _buildIdHash = null;
        private MachineLocation[] _buildRingMachines = CollectionUtilities.EmptyArray<MachineLocation>();
        private readonly ConcurrentBigSet<ContentHash> _pendingProactivePuts = new ConcurrentBigSet<ContentHash>();
        private readonly ResultNagleQueue<ContentHash, ContentHashWithSizeAndLocations> _proactiveCopyGetBulkNagleQueue;

        // The method used for remote pins depends on which pin configuration is enabled.
        private readonly RemotePinAsync _remotePinner;

        /// <summary>
        /// The store that persists content locations to a persistent store.
        /// </summary>
        internal readonly IContentLocationStore ContentLocationStore;

        /// <summary>
        /// The machine location for the current cache.
        /// </summary>
        protected readonly MachineLocation LocalCacheRootMachineLocation;

        /// <summary>
        /// The content session that actually stores content.
        /// </summary>
        public IContentSession Inner { get; }

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(DistributedContentSession<T>));

        /// <nodoc />
        protected readonly DistributedContentCopier<T> DistributedCopier;

        private readonly IDistributedContentCopierHost _copierHost;

        /// <summary>
        /// Settings for the session.
        /// </summary>
        protected readonly DistributedContentStoreSettings Settings;

        /// <summary>
        /// Trace only stops and errors to reduce the Kusto traffic.
        /// </summary>
        protected override bool TraceOperationStarted => false;

        /// <summary>
        /// Semaphore that limits the maximum number of concurrent put and place operations
        /// </summary>
        protected readonly SemaphoreSlim PutAndPlaceFileGate;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadOnlyDistributedContentSession{T}"/> class.
        /// </summary>
        public ReadOnlyDistributedContentSession(
            string name,
            IContentSession inner,
            IContentLocationStore contentLocationStore,
            DistributedContentCopier<T> contentCopier,
            IDistributedContentCopierHost copierHost,
            MachineLocation localMachineLocation,
            DistributedContentStoreSettings? settings = default)
            : base(name)
        {
            Contract.Requires(name != null);
            Contract.Requires(inner != null);
            Contract.Requires(contentLocationStore != null);
            Contract.Requires(contentLocationStore is StartupShutdownSlimBase, "The store must derive from StartupShutdownSlimBase");
            Contract.Requires(localMachineLocation.IsValid);

            Inner = inner;
            ContentLocationStore = contentLocationStore;
            LocalCacheRootMachineLocation = localMachineLocation;
            Settings = settings ?? DistributedContentStoreSettings.DefaultSettings;
            _copierHost = copierHost;
            _remotePinner = PinFromMultiLevelContentLocationStore;
            DistributedCopier = contentCopier;
            PutAndPlaceFileGate = new SemaphoreSlim(Settings.MaximumConcurrentPutAndPlaceFileOperations);

            _proactiveCopyGetBulkNagleQueue = new ResultNagleQueue<ContentHash, ContentHashWithSizeAndLocations>(
                maxDegreeOfParallelism: 1,
                interval: Settings.ProactiveCopyGetBulkInterval,
                batchSize: Settings.ProactiveCopyGetBulkBatchSize);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var canHibernate = Inner is IHibernateContentSession ? "can" : "cannot";
            Tracer.Debug(context, $"Session {Name} {canHibernate} hibernate");
            await Inner.StartupAsync(context).ThrowIfFailure();

            _proactiveCopyGetBulkNagleQueue.Start(hashes => GetLocationsForProactiveCopyAsync(context.CreateNested(Tracer.Name), hashes));

            TryRegisterMachineWithBuildId(context);

            return BoolResult.Success;
        }

        private void TryRegisterMachineWithBuildId(OperationContext context)
        {
            if (Constants.TryExtractBuildId(Name, out _buildId) && Guid.TryParse(_buildId, out var buildIdGuid))
            {
                // Generate a fake hash for the build and register a content entry in the location store to represent
                // machines in the build ring
                _buildIdHash = new ContentHash(HashType.MD5, buildIdGuid.ToByteArray());

                var arguments = $"Build={_buildId}, BuildIdHash={_buildIdHash.Value.ToShortString()}";

                context.PerformOperationAsync(Tracer, async () =>
                {
                    await ContentLocationStore.RegisterLocalLocationAsync(context, new[] { new ContentHashWithSize(_buildIdHash.Value, _buildId.Length) }, context.Token, UrgencyHint.Nominal).ThrowIfFailure();

                    return BoolResult.Success;
                },
                extraStartMessage: arguments,
                extraEndMessage: r => arguments).FireAndForget(context);
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var counterSet = new CounterSet();
            counterSet.Merge(GetCounters(), $"{Tracer.Name}.");

            // Unregister from build machine location set
            if (_buildIdHash.HasValue)
            {
                await ContentLocationStore.TrimBulkAsync(context, new[] { _buildIdHash.Value }, context.Token, UrgencyHint.Nominal)
                    .IgnoreErrorsAndReturnCompletion();
            }

            await Inner.ShutdownAsync(context).ThrowIfFailure();

            _proactiveCopyGetBulkNagleQueue.Dispose();
            Tracer.TraceStatisticsAtShutdown(context, counterSet, prefix: "DistributedContentSessionStats");

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            base.DisposeCore();

            Inner.Dispose();
        }

        /// <inheritdoc />
        protected override async Task<PinResult> PinCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            // Call bulk API
            var result = await PinHelperAsync(operationContext, new[] { contentHash }, urgencyHint, PinOperationConfiguration.Default());
            return (await result.First()).Item;
        }

        /// <inheritdoc />
        protected override async Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            OpenStreamResult streamResult =
                await Inner.OpenStreamAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
            if (streamResult.Code == OpenStreamResult.ResultCode.Success)
            {
                return streamResult;
            }

            long? size = null;
            GetBulkLocationsResult? localGetBulkResult = null;

            // First try to fetch file based on locally stored locations for the hash
            // Then fallback to fetching file based on global locations  (i.e. Redis) minus the locally stored locations which were already checked
            foreach (var getBulkTask in ContentLocationStore.MultiLevelGetLocations(operationContext, new[] { contentHash }, operationContext.Token, urgencyHint, subtractLocalResults: true))
            {
                var getBulkResult = await getBulkTask;
                // There is an issue with GetBulkLocationsResult construction from Exception that may loose the information about the origin.
                // So we rely on the result order of MultiLevelGetLocations method: the first result is always local and the second one is global.

                GetBulkOrigin origin = localGetBulkResult == null ? GetBulkOrigin.Local : GetBulkOrigin.Global;
                if (origin == GetBulkOrigin.Local)
                {
                    localGetBulkResult = getBulkResult;
                }

                // Local function: Use content locations for GetBulk to copy file locally
                async Task<BoolResult> tryCopyContentLocalAsync()
                {
                    if (!getBulkResult || !getBulkResult.ContentHashesInfo.Any())
                    {
                        return new BoolResult($"Metadata records for hash {contentHash.ToShortString()} not found in content location store.");
                    }

                    // Don't reconsider locally stored results that were checked in prior iteration
                    getBulkResult = getBulkResult.Subtract(localGetBulkResult);

                    var hashInfo = getBulkResult.ContentHashesInfo.Single();

                    var checkBulkResult = CheckBulkResult(operationContext, hashInfo, log: getBulkResult.Origin == GetBulkOrigin.Global);
                    if (!checkBulkResult.Succeeded)
                    {
                        return new BoolResult(checkBulkResult);
                    }

                    var copyResult = await TryCopyAndPutAsync(operationContext, hashInfo, urgencyHint, CopyReason.OpenStream, trace: false);
                    if (!copyResult)
                    {
                        return new BoolResult(copyResult);
                    }

                    size = copyResult.ContentSize;
                    return BoolResult.Success;
                }

                var copyLocalResult = await tryCopyContentLocalAsync();

                // Throw operation canceled to avoid operations below which are not value for canceled case.
                operationContext.Token.ThrowIfCancellationRequested();

                if (copyLocalResult.Succeeded)
                {
                    // Succeeded in copying content locally. No need to try with more content locations
                    break;
                }
                else if (origin == GetBulkOrigin.Global)
                {
                    return new OpenStreamResult(copyLocalResult, OpenStreamResult.ResultCode.ContentNotFound);
                }
            }

            Contract.Assert(size != null, "Size should be set if operation succeeded");

            var updateResult = await UpdateContentTrackerWithNewReplicaAsync(operationContext, new[] { new ContentHashWithSize(contentHash, size.Value) }, urgencyHint);
            if (!updateResult.Succeeded)
            {
                return new OpenStreamResult(updateResult);
            }

            return await Inner.OpenStreamAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        Task<IEnumerable<Task<Indexed<PinResult>>>> IConfigurablePin.PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration pinOperationConfiguration)
        {
            // The lifetime of this operation should be detached from the lifetime of the session.
            // But still tracking the lifetime of the store.
            return WithStoreCancellationAsync(context,
                opContext => PinHelperAsync(opContext, contentHashes, pinOperationConfiguration.UrgencyHint, pinOperationConfiguration),
                pinOperationConfiguration.CancellationToken);
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, Counter retryCounter, Counter fileCounter)
        {
            return PinHelperAsync(operationContext, contentHashes, urgencyHint, PinOperationConfiguration.Default());
        }

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> PinHelperAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, PinOperationConfiguration pinOperationConfiguration)
        {
            Contract.Requires(contentHashes != null);

            IEnumerable<Task<Indexed<PinResult>>>? pinResults = null;

            IEnumerable<Task<Indexed<PinResult>>>? intermediateResult = null;
            if (pinOperationConfiguration.ReturnGlobalExistenceFast)
            {
                Tracer.Debug(operationContext.TracingContext, $"Detected {nameof(PinOperationConfiguration.ReturnGlobalExistenceFast)}");

                // Check globally for existence, but do not copy locally and do not update content tracker.
                pinResults = await Workflows.RunWithFallback(
                    contentHashes,
                    async hashes =>
                    {
                        intermediateResult = await Inner.PinAsync(operationContext, hashes, operationContext.Token, urgencyHint);
                        return intermediateResult;
                    },
                    hashes => _remotePinner(operationContext, hashes, succeedWithOneLocation: true, urgencyHint),
                    result => result.Succeeded);

                // Replace operation context with a new cancellation token so it can outlast this call.
                // Using a cancellation token from the store avoid the operations lifetime to be greater than the lifetime of the outer store.
                operationContext = new OperationContext(operationContext.TracingContext, token: StoreShutdownStartedCancellationToken);
            }

            // Default pin action
            var pinTask = Workflows.RunWithFallback(
                    contentHashes,
                    hashes => intermediateResult == null ? Inner.PinAsync(operationContext, hashes, operationContext.Token, urgencyHint) : Task.FromResult(intermediateResult),
                    hashes => _remotePinner(operationContext, hashes, succeedWithOneLocation: false, urgencyHint),
                    result => result.Succeeded,
                    // Exclude the empty hash because it is a special case which is hard coded for place/openstream/pin.
                    async hits => await UpdateContentTrackerWithLocalHitsAsync(operationContext, hits.Where(x => !contentHashes[x.Index].IsEmptyHash()).Select(x => new ContentHashWithSizeAndLastAccessTime(contentHashes[x.Index], x.Item.ContentSize, x.Item.LastAccessTime)).ToList(), urgencyHint));

            // Initiate a proactive copy if just pinned content is under-replicated
            if (Settings.ProactiveCopyOnPin && Settings.ProactiveCopyMode != ProactiveCopyMode.Disabled)
            {
                pinTask = ProactiveCopyOnPinAsync(operationContext, contentHashes, pinTask);
            }

            if (pinOperationConfiguration.ReturnGlobalExistenceFast)
            {
                // Fire off the default pin action, but do not await the result.
                operationContext.PerformNonResultOperationAsync(
                    Tracer,
                    () => pinTask,
                    extraEndMessage: results =>
                    {
                        var resultString = string.Join(",", results.Select(async task =>
                        {
                            // Since all bulk operations are constructed with Task.FromResult, it is safe to just access the result;
                            Indexed<PinResult>? result = await task;
                            return result != null ? $"{contentHashes[result.Index].ToShortString()}:{result.Item}" : string.Empty;
                        }));

                        return $"ConfigurablePin Count={contentHashes.Count}, Hashes=[{resultString}]";
                    },
                    traceErrorsOnly: TraceErrorsOnly,
                    traceOperationStarted: TraceOperationStarted,
                    traceOperationFinished: true,
                    isCritical: false).FireAndForget(operationContext);
            }
            else
            {
                pinResults = await pinTask;
            }

            Contract.Assert(pinResults != null);
            return pinResults;
        }

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> ProactiveCopyOnPinAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, Task<IEnumerable<Task<Indexed<PinResult>>>> pinTask)
        {
            var results = await pinTask;

            // Since the rest of the operation is done asynchronously, using a cancellation context from the outer store.
            var proactiveCopyTask = WithStoreCancellationAsync(
                context,
                async opContext =>
                {
                    var proactiveTasks = results.Select(resultTask => proactiveCopyOnSinglePinAsync(opContext, resultTask)).ToList();

                    // Ensure all tasks are completed, after awaiting outer task
                    await Task.WhenAll(proactiveTasks);

                    return proactiveTasks;
                });

            if (Settings.InlineOperationsForTests)
            {
                return await proactiveCopyTask;
            }
            else
            {
                proactiveCopyTask.FireAndForget(context);
                return results;
            }

            // Proactive copy an individual pin
            async Task<Indexed<PinResult>> proactiveCopyOnSinglePinAsync(OperationContext opContext, Task<Indexed<PinResult>> resultTask)
            {
                Indexed<PinResult> indexedPinResult = await resultTask;
                var pinResult = indexedPinResult.Item;

                // Local pins and distributed pins which are copied locally allow proactive copy
                if (pinResult.Succeeded && (!(pinResult is DistributedPinResult distributedPinResult) || distributedPinResult.CopyLocally))
                {
                    var proactiveCopyResult = await ProactiveCopyIfNeededAsync(opContext, contentHashes[indexedPinResult.Index], tryBuildRing: true, CopyReason.Pin);

                    // Only fail if all copies failed.
                    if (!proactiveCopyResult.Succeeded)
                    {
                        return new PinResult(proactiveCopyResult).WithIndex(indexedPinResult.Index);
                    }
                }

                return indexedPinResult;
            }
        }

        /// <inheritdoc />
        protected override async Task<PlaceFileResult> PlaceFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var resultWithData = await PerformPlaceFileGatedOperationAsync(
                operationContext,
                () => PlaceHelperAsync(
                    operationContext,
                    new[] { new ContentHashWithPath(contentHash, path) },
                    accessMode,
                    replacementMode,
                    realizationMode,
                    urgencyHint,
                    retryCounter
                ));

            var result = await resultWithData.Result.SingleAwaitIndexed();
            result.Metadata = resultWithData.Metadata;
            return result;
        }

        /// <inheritdoc />
        protected override async Task<PlaceBulkResult> PlaceFileCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            // The fallback is invoked for cache misses only. This preserves existing behavior of
            // bubbling up errors with Inner store instead of trying remote.
            var resultWithData = await PerformPlaceFileGatedOperationAsync(
                operationContext,
                () => PlaceHelperAsync(
                    operationContext,
                    hashesWithPaths,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    urgencyHint,
                    retryCounter
                ));

            // We are trying tracing here we did not want to change the signature for PlaceFileCoreAsync, which is implemented in multiple locations
            operationContext.TraceDebug($"PlaceFileBulk, Gate.OccupiedCount={resultWithData.Metadata.GateOccupiedCount} Gate.Wait={resultWithData.Metadata.GateWaitTime.TotalMilliseconds}ms");

            return resultWithData.Result;
        }

        private Task<PlaceBulkResult> PlaceHelperAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return Workflows.RunWithFallback(
                hashesWithPaths,
                args => Inner.PlaceFileAsync(
                    operationContext,
                    args,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    operationContext.Token,
                    urgencyHint),
                args => fetchFromMultiLevelContentLocationStoreThenPlaceFileAsync(args),
                result => IsPlaceFileSuccess(result),
                async hits => await UpdateContentTrackerWithLocalHitsAsync(
                    operationContext,
                    hits.Select(
                            x => new ContentHashWithSizeAndLastAccessTime(
                                hashesWithPaths[x.Index].Hash,
                                x.Item.FileSize,
                                x.Item.LastAccessTime))
                        .ToList(),
                    urgencyHint));

            Task<PlaceBulkResult> fetchFromMultiLevelContentLocationStoreThenPlaceFileAsync(IReadOnlyList<ContentHashWithPath> fetchedContentInfo)
            {
                return MultiLevelUtilities.RunMultiLevelAsync(
                    fetchedContentInfo,
                    runFirstLevelAsync: args => FetchFromMultiLevelContentLocationStoreThenPutAsync(operationContext, args, urgencyHint, CopyReason.Place),
                    runSecondLevelAsync: args => Inner.PlaceFileAsync(operationContext, args, accessMode, replacementMode, realizationMode, operationContext.Token, urgencyHint),
                    // NOTE: We just use the first level result if the the fetch using content location store fails because the place cannot succeed since the
                    // content will not have been put into the local CAS
                    useFirstLevelResult: result => !IsPlaceFileSuccess(result));
            }
        }

        private Task<ResultWithMetaData<PlaceBulkResult>> PerformPlaceFileGatedOperationAsync(OperationContext operationContext, Func<Task<PlaceBulkResult>> func, bool bulkPlace = true)
        {
            return PutAndPlaceFileGate.GatedOperationAsync(async (timeWaiting, currentCount) =>
           {
               var gateOccupiedCount = Settings.MaximumConcurrentPutAndPlaceFileOperations - currentCount;

               var result = await func();

               return new ResultWithMetaData<PlaceBulkResult>(
                   new ResultMetaData(timeWaiting, gateOccupiedCount),
                   result);
           }, operationContext.Token);
        }

        private static bool IsPlaceFileSuccess(PlaceFileResult result)
        {
            return result.Code != PlaceFileResult.ResultCode.Error && result.Code != PlaceFileResult.ResultCode.NotPlacedContentNotFound;
        }

        /// <inheritdoc />
        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return Inner is IHibernateContentSession session
                ? session.EnumeratePinnedContentHashes()
                : Enumerable.Empty<ContentHash>();
        }

        /// <inheritdoc />
        public Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            // TODO: Replace PinBulkAsync in hibernate with PinAsync bulk call (bug 1365340)
            return Inner is IHibernateContentSession session
                ? session.PinBulkAsync(context, contentHashes)
                : Task.FromResult(0);
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownEvictionAsync(Context context)
        {
            return Inner is IHibernateContentSession session
                ? session.ShutdownEvictionAsync(context)
                : BoolResult.SuccessTask;
        }

        private Task<PlaceBulkResult> FetchFromMultiLevelContentLocationStoreThenPutAsync(
            OperationContext context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            UrgencyHint urgencyHint,
            CopyReason reason)
        {
            // First try to place file by fetching files based on locally stored locations for the hash
            // Then fallback to fetching file based on global locations  (i.e. Redis) minus the locally stored locations which were already checked

            var localGetBulkResult = new BuildXL.Utilities.AsyncOut<GetBulkLocationsResult>();

            return Workflows.RunWithFallback(
                hashesWithPaths,
                initialFunc: async args =>
                {
                    var contentHashes = args.Select(p => p.Hash).ToList();
                    localGetBulkResult.Value = await ContentLocationStore.GetBulkAsync(context, contentHashes, context.Token, urgencyHint, GetBulkOrigin.Local);
                    return await FetchFromContentLocationStoreThenPutAsync(context, args, isLocal: true, urgencyHint, localGetBulkResult.Value, reason);
                },
                fallbackFunc: async args =>
                {
                    var contentHashes = args.Select(p => p.Hash).ToList();
                    var globalGetBulkResult = await ContentLocationStore.GetBulkAsync(context, contentHashes, context.Token, urgencyHint, GetBulkOrigin.Global);
                    globalGetBulkResult = globalGetBulkResult.Subtract(localGetBulkResult.Value);
                    return await FetchFromContentLocationStoreThenPutAsync(context, args, isLocal: false, urgencyHint, globalGetBulkResult, reason);
                },
                isSuccessFunc: result => IsPlaceFileSuccess(result));
        }

        private async Task<PlaceBulkResult> FetchFromContentLocationStoreThenPutAsync(
            OperationContext context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            bool isLocal,
            UrgencyHint urgencyHint,
            GetBulkLocationsResult getBulkResult,
            CopyReason reason)
        {
            try
            {
                // Tracing the hashes here for the entire list, instead of tracing one hash at a time inside TryCopyAndPutAsync method.

                // This returns failure if any item in the batch wasn't copied locally
                // TODO: split results and call PlaceFile on successfully copied files (bug 1365340)
                if (!getBulkResult.Succeeded || !getBulkResult.ContentHashesInfo.Any())
                {
                    return hashesWithPaths.Select(
                            p => new PlaceFileResult(
                                getBulkResult,
                                PlaceFileResult.ResultCode.NotPlacedContentNotFound,
                                "Metadata records not found in content location store"))
                        .AsIndexedTasks();
                }

                // TransformBlock is supposed to return items in FIFO order, so we don't need to index the input
                var copyFilesLocallyBlock =
                    new TransformBlock<Indexed<ContentHashWithSizeAndLocations>, Indexed<PlaceFileResult>>(
                        async indexed =>
                        {
                            var contentHashWithSizeAndLocations = indexed.Item;
                            PlaceFileResult result;
                            if (contentHashWithSizeAndLocations.Locations == null)
                            {
                                var message = $"No replicas ever registered for hash {contentHashWithSizeAndLocations.ContentHash.ToShortString()}";

                                if (isLocal)
                                {
                                    // Trace only for locations obtained from the local store.
                                    Tracer.Warning(context, message);
                                }

                                result = new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, message);
                            }
                            else if (contentHashWithSizeAndLocations.Locations.Count == 0)
                            {
                                var message = $"No replicas exist currently in content tracker for hash {contentHashWithSizeAndLocations.ContentHash.ToShortString()}";

                                if (isLocal)
                                {
                                    // Trace only for locations obtained from the local store.
                                    Tracer.Warning(context, message);
                                }

                                result = new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, message);
                            }
                            else
                            {
                                var putResult = await TryCopyAndPutAsync(
                                    context,
                                    contentHashWithSizeAndLocations,
                                    urgencyHint,
                                    reason,
                                    // We just traced all the hashes as a result of GetBulk call, no need to trace each individual hash.
                                    trace: false);
                                if (!putResult)
                                {
                                    result = new PlaceFileResult(putResult);
                                }
                                else
                                {
                                    result = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithMove, putResult.ContentSize);
                                }

                            }

                            return result.WithIndex(indexed.Index);
                        },
                        new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Settings.ParallelCopyFilesLimit, });

                // TODO: Better way ? (bug 1365340)
                copyFilesLocallyBlock.PostAll(getBulkResult.ContentHashesInfo.AsIndexed());
                var copyFilesLocally =
                    await Task.WhenAll(
                        Enumerable.Range(0, getBulkResult.ContentHashesInfo.Count).Select(i => copyFilesLocallyBlock.ReceiveAsync(context.Token)));
                copyFilesLocallyBlock.Complete();

                var updateResults = await UpdateContentTrackerWithNewReplicaAsync(
                    context,
                    copyFilesLocally.Where(r => r.Item.Succeeded).Select(r => new ContentHashWithSize(hashesWithPaths[r.Index].Hash, r.Item.FileSize)).ToList(),
                    urgencyHint);

                if (!updateResults.Succeeded)
                {
                    return copyFilesLocally.Select(result => new PlaceFileResult(updateResults).WithIndex(result.Index)).AsTasks();
                }

                return copyFilesLocally.AsTasks();
            }
            catch (Exception ex)
            {
                return hashesWithPaths.Select((hash, index) => new PlaceFileResult(ex).WithIndex(index)).AsTasks();
            }
        }

        private BoolResult CheckBulkResult(Context context, ContentHashWithSizeAndLocations result, bool log = true)
        {
            // Null represents no replicas were ever registered, where as empty list implies content is missing from all replicas
            if (result.Locations == null)
            {
                if (log)
                {
                    Tracer.Warning(context, $"No replicas found in content tracker for hash {result.ContentHash.ToShortString()}");
                }

                return new BoolResult($"No replicas registered for hash");
            }

            if (!result.Locations.Any())
            {
                if (log)
                {
                    Tracer.Warning(context, $"No replicas currently exist in content tracker for hash {result.ContentHash.ToShortString()}");
                }

                return new BoolResult($"Content for hash is missing from all replicas");
            }

            return BoolResult.Success;
        }

        private async Task<PutResult> TryCopyAndPutAsync(OperationContext operationContext, ContentHashWithSizeAndLocations hashInfo, UrgencyHint urgencyHint, CopyReason reason, bool trace)
        {
            Context context = operationContext;
            CancellationToken cts = operationContext.Token;

            if (trace)
            {
                Tracer.Debug(operationContext, $"Copying {hashInfo.ContentHash.ToShortString()} with {hashInfo.Locations?.Count ?? 0 } locations");
            }

            if (ContentLocationStore.AreBlobsSupported && hashInfo.Size > 0 && hashInfo.Size <= ContentLocationStore.MaxBlobSize)
            {
                var smallFileResult = await ContentLocationStore.GetBlobAsync(operationContext, hashInfo.ContentHash);

                if (smallFileResult.Succeeded && smallFileResult.Blob != null)
                {
                    using (var stream = new MemoryStream(smallFileResult.Blob))
                    {
                        return await Inner.PutStreamAsync(context, hashInfo.ContentHash, stream, cts, urgencyHint);

                    }
                }
            }

            byte[]? bytes = null;

            var putResult = await DistributedCopier.TryCopyAndPutAsync(
                operationContext,
                _copierHost,
                hashInfo,
                reason,
                handleCopyAsync: async args =>
                {
                    (CopyFileResult copyFileResult, AbsolutePath tempLocation, int attemptCount) = args;

                    PutResult innerPutResult;
                    long actualSize = copyFileResult.Size ?? hashInfo.Size;
                    if (Settings.UseTrustedHash(actualSize) && Inner is ITrustedContentSession trustedInner)
                    {
                        // The file has already been hashed, so we can trust the hash of the file.
                         innerPutResult = await trustedInner.PutTrustedFileAsync(
                             context,
                             new ContentHashWithSize(hashInfo.ContentHash, actualSize),
                             tempLocation,
                             FileRealizationMode.Move,
                             cts,
                             urgencyHint);

                        // BytesFromTrustedCopy will only be non-null when the trusted copy exposes the bytes it copied because AreBlobsSupported evaluated to true
                        //  and the file size is smaller than BlobMaxSize.
                        if (innerPutResult && copyFileResult.BytesFromTrustedCopy != null)
                        {
                            bytes = copyFileResult.BytesFromTrustedCopy;
                        }
                    }
                    else
                    {
                        // Pass the HashType, not the Hash. This prompts a re-hash of the file, which places it where its actual hash requires.
                        // If the actual hash differs from the expected hash, then we fail below and move to the next location.
                        // Also, record the bytes if the file is small enough to be put into the ContentLocationStore.
                        if (actualSize >= 0 && actualSize <= ContentLocationStore.MaxBlobSize &&
                            ContentLocationStore.AreBlobsSupported && Inner is IDecoratedStreamContentSession decoratedStreamSession)
                        {
                            RecordingStream? recorder = null;
                            innerPutResult = await decoratedStreamSession.PutFileAsync(
                                context,
                                tempLocation,
                                hashInfo.ContentHash.HashType,
                                FileRealizationMode.Move,
                                cts,
                                urgencyHint,
                                stream =>
                                {
                                    recorder = RecordingStream.ReadRecordingStream(inner: stream, size: actualSize);
                                    return recorder;
                                });

                            if (innerPutResult && recorder != null)
                            {
                                bytes = recorder.RecordedBytes;
                            }
                        }
                        else
                        {
                             innerPutResult = await Inner.PutFileAsync(
                                 context,
                                 hashInfo.ContentHash.HashType,
                                 tempLocation,
                                 FileRealizationMode.Move,
                                 cts,
                                 urgencyHint);
                        }
                    }

                    return innerPutResult;

                });

                if (bytes != null && putResult.Succeeded)
                {
                    await PutBlobAsync(operationContext, putResult.ContentHash, bytes);
                }

                return putResult;
            }

        /// <summary>
        /// Puts a given blob into redis (either inline or not depending on the settings).
        /// </summary>
        /// <remarks>
        /// This method won't fail, because all the errors are traced and swallowed.
        /// </remarks>
        protected async Task PutBlobAsync(OperationContext context, ContentHash contentHash, byte[] bytes)
        {
            if (Settings.InlineOperationsForTests)
            {
                // Failures already traced. No need to trace it here one more time.
                await ContentLocationStore.PutBlobAsync(context, contentHash, bytes).IgnoreFailure();
            }
            else
            {
                // Fire and forget since this step is optional.
                // Since the rest of the operation is done asynchronously, create new context to stop cancelling operation prematurely.
                WithStoreCancellationAsync(
                        context,
                        opContext => ContentLocationStore.PutBlobAsync(opContext, contentHash, bytes)
                    )
                    // Tracing unhandled errors only because normal failures already traced by the operation provider.
                    .TraceIfFailure(context, failureSeverity: Severity.Debug, traceTaskExceptionsOnly: true, operation: "PutBlobAsync");
            }
        }

        /// <summary>
        /// Runs a given function in the cancellation context of the outer store.
        /// </summary>
        /// <remarks>
        /// The lifetime of some session-based operation is longer than the session itself.
        /// For instance, proactive copies, or put blob can outlive the lifetime of the session.
        /// But these operations should not outlive the lifetime of the store, because when the store is closed
        /// most likely all the operations will fail with some weird errors like "ObjectDisposedException".
        ///
        /// This helper method allows running the operations that may outlive the lifetime of the session but should not outlive the lifetime of the store.
        /// </remarks>
        protected Task<TResult> WithStoreCancellationAsync<TResult>(Context context, Func<OperationContext, Task<TResult>> func, CancellationToken token = default)
        {
            return ((StartupShutdownSlimBase)ContentLocationStore).WithOperationContext(context, token, func);
        }

        /// <summary>
        /// Returns a cancellation token that is triggered when the outer store shutdown is started.
        /// </summary>
        protected CancellationToken StoreShutdownStartedCancellationToken =>
            ((StartupShutdownSlimBase)ContentLocationStore).ShutdownStartedCancellationToken;

        private Task<BoolResult> UpdateContentTrackerWithNewReplicaAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentHashes, UrgencyHint urgencyHint)
        {
            if (contentHashes.Count == 0)
            {
                return BoolResult.SuccessTask;
            }

            // TODO: Pass location store option (seems to only be used to prevent updating TTL when replicating for proactive replication) (bug 1365340)
            return ContentLocationStore.RegisterLocalLocationAsync(context, contentHashes, context.Token, urgencyHint);
        }

        private Task<IEnumerable<Task<Indexed<PinResult>>>> PinFromMultiLevelContentLocationStore(
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes,
            bool succeedWithOneLocation,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return Workflows.RunWithFallback(
                contentHashes,
                hashes => PinFromContentLocationStoreOriginAsync(context, hashes, GetBulkOrigin.Local, succeedWithOneLocation: succeedWithOneLocation, urgencyHint),
                hashes => PinFromContentLocationStoreOriginAsync(context, hashes, GetBulkOrigin.Global, succeedWithOneLocation: succeedWithOneLocation, urgencyHint),
                result => result.Succeeded);
        }

        // This method creates pages of hashes, makes one bulk call to the content location store to get content location record sets for all the hashes on the page,
        // and fires off processing of the returned content location record sets while proceeding to the next page of hashes in parallel.
        private async Task<IEnumerable<Task<Indexed<PinResult>>>> PinFromContentLocationStoreOriginAsync(OperationContext operationContext, IReadOnlyList<ContentHash> hashes, GetBulkOrigin origin, bool succeedWithOneLocation, UrgencyHint urgency = UrgencyHint.Nominal)
        {
            CancellationToken cancel = operationContext.Token;
            // Create an action block to process all the requested remote pins while limiting the number of simultaneously executed.
            var pinnings = new List<RemotePinning>(hashes.Count);
            var pinningOptions = new ExecutionDataflowBlockOptions() { CancellationToken = cancel, MaxDegreeOfParallelism = Settings.PinConfiguration?.MaxIOOperations ?? 1 };
            var pinningAction = new ActionBlock<RemotePinning>(async pinning => await PinRemoteAsync(operationContext, pinning, isLocal: origin == GetBulkOrigin.Local, updateContentTracker: false, succeedWithOneLocation: succeedWithOneLocation), pinningOptions);

            // Process the requests in pages so we can make bulk calls, but not too big bulk calls, to the content location store.
            foreach (IReadOnlyList<ContentHash> pageHashes in hashes.GetPages(ContentLocationStore.PageSize))
            {
                // Make a bulk call to content location store to get location records for all hashes on the page.
                // NOTE: We use GetBulkStackedAsync so that when Global results are retrieved we also include Local results to ensure we get a full view of available content
                GetBulkLocationsResult pageLookup = await ContentLocationStore.GetBulkStackedAsync(operationContext, pageHashes, cancel, urgency, origin);

                // If successful, fire off the remote pinning logic for each hash. If not, set all pins to failed.
                if (pageLookup.Succeeded)
                {
                    foreach (ContentHashWithSizeAndLocations record in pageLookup.ContentHashesInfo)
                    {
                        RemotePinning pinning = new RemotePinning(record);
                        pinnings.Add(pinning);
                        bool accepted = await pinningAction.SendAsync(pinning, cancel);
                        Contract.Assert(accepted);
                    }
                }
                else
                {
                    foreach (ContentHash hash in pageHashes)
                    {
                        Tracer.Warning(operationContext, $"Pin failed for hash {hash.ToShortString()}: directory query failed with error {pageLookup.ErrorMessage}");
                        RemotePinning pinning = new RemotePinning(new ContentHashWithSizeAndLocations(hash, -1L))
                        {
                            Result = new PinResult(pageLookup)
                        };
                        pinnings.Add(pinning);
                    }
                }
            }

            Contract.Assert(pinnings.Count == hashes.Count);

            // Wait for all the pinning actions to complete.
            pinningAction.Complete();

            try
            {
                await pinningAction.Completion;
            }
            catch (TaskCanceledException)
            {
                // Cancellation token provided to an action block can be canceled.
                // Ignoring the exception in this case.
            }

            // Inform the content directory that we copied the files.
            // Looking for distributed pin results that were successful by copying the content locally.
            var localCopies = pinnings.Select((rp, index) => (result: rp, index)).Where(x => x.result.Result is DistributedPinResult dpr && dpr.CopyLocally).ToList();

            BoolResult updated = await UpdateContentTrackerWithNewReplicaAsync(operationContext, localCopies.Select(lc => new ContentHashWithSize(lc.result.Record.ContentHash, lc.result.Record.Size)).ToList(), UrgencyHint.Nominal);
            if (!updated.Succeeded)
            {
                // We failed to update the tracker. Need to update the results.
                string hashesAsString = string.Join(", ", localCopies.Select(lc => lc.result.Record.ContentHash.ToShortString()));
                Tracer.Warning(operationContext, $"Pin failed for hashes {hashesAsString}: local copy succeeded, but could not inform content directory due to {updated.ErrorMessage}.");
                foreach (var (_, index) in localCopies)
                {
                    pinnings[index].Result = new PinResult(updated);
                }
            }

            // The return type should probably be just Task<IList<PinResult>>, but higher callers require the Indexed wrapper and that the PinResults be encased in Tasks.
            return pinnings.Select(x => x.Result ?? createCanceledPutResult()).AsIndexed().AsTasks();

            static PinResult createCanceledPutResult() => new ErrorResult("The operation was canceled").AsResult<PinResult>();
        }

        // The dataflow framework can process only a single object, and returns no output from that processing. By combining the input and output of each remote pinning into a single object,
        // we can nonetheless use the dataflow framework to process pinnings and read the output from the updated objects afterward.
        private class RemotePinning
        {
            public ContentHashWithSizeAndLocations Record { get; }

            public PinResult? Result { get; set; }

            public RemotePinning(ContentHashWithSizeAndLocations record)
                => Record = record;
        }

        // This method processes each remote pinning, setting the output when the operation is completed.
        private async Task PinRemoteAsync(
            OperationContext context,
            RemotePinning pinning,
            bool isLocal,
            bool updateContentTracker = true,
            bool succeedWithOneLocation = false)
        {
            PinResult result = await PinRemoteAsync(context, pinning.Record, isLocal, updateContentTracker, succeedWithOneLocation: succeedWithOneLocation);
            pinning.Result = result;
        }

        // This method processes a single content location record set for pinning.
        private async Task<PinResult> PinRemoteAsync(
            OperationContext operationContext,
            ContentHashWithSizeAndLocations remote,
            bool isLocal,
            bool updateContentTracker = true,
            bool succeedWithOneLocation = false)
        {
            IReadOnlyList<MachineLocation>? locations = remote.Locations;

            // If no remote locations are recorded, we definitely can't pin
            if (locations == null || locations.Count == 0)
            {
                if (!isLocal)
                {
                    // Trace only when pin failed based on the data from the global store.
                    Tracer.Warning(operationContext, $"Pin failed for hash {remote.ContentHash.ToShortString()}: no remote records.");
                }

                return DistributedPinResult.ContentNotFound(replicaCount: 0, "No locations found");
            }

            // When we only require the content to exist at least once anywhere, we can ignore pin thresholds
            // and return success after finding a single location.
            if (succeedWithOneLocation && locations.Count >= 1)
            {
                return DistributedPinResult.EnoughReplicas(locations.Count, "Global succeeds");
            }

            if (locations.Count >= Settings.PinConfiguration.PinMinUnverifiedCount)
            {
                SessionCounters[Counters.PinUnverifiedCountSatisfied].Increment();
                var result = DistributedPinResult.EnoughReplicas(locations.Count);

                // Triggering an async copy if the number of replicas are close to a PinMinUnverifiedCount threshold.
                int threshold = Settings.PinConfiguration.PinMinUnverifiedCount +
                                Settings.PinConfiguration.AsyncCopyOnPinThreshold;
                if (locations.Count < threshold)
                {
                    Tracer.Info(operationContext, $"Starting asynchronous copy of the content for hash {remote.ContentHash.ToShortString()} because the number of locations '{locations.Count}' is less then a threshold of '{threshold}'.");
                    SessionCounters[Counters.StartCopyForPinWhenUnverifiedCountSatisfied].Increment();

                    // For "synchronous" pins the tracker is updated at once for all the hashes for performance reasons,
                    // but for asynchronous copy we always need to update the tracker with a new location.
                    var task = WithStoreCancellationAsync(
                        operationContext.TracingContext,
                        opContext => TryCopyAndPutAndUpdateContentTrackerAsync(opContext, remote, updateContentTracker: true, CopyReason.AsyncPin));
                    if (Settings.InlineOperationsForTests)
                    {
                        (await task).TraceIfFailure(operationContext);
                    }
                    else
                    {
                        task.FireAndForget(operationContext.TracingContext, operation: "AsynchronousCopyOnPin");
                    }

                    // Note: Pin result traces with CpA (copied asynchronously) code is to provide the information that the content is being copied asynchronously, and that replica count is enough but not above async copy threshold.
                    // This trace result does not represent that of the async copy since that is done FireAndForget.
                    result = DistributedPinResult.AsynchronousCopy(locations.Count);
                }

                return result;
            }

            if (isLocal)
            {
                // Don't copy content locally based on locally cached result. So stop here and return content not found.
                // This method will be called again with global locations at which time we will attempt to copy the files locally.
                // When allowing global locations to succeed a put, report success.
                return DistributedPinResult.ContentNotFound(locations.Count);
            }

            // Previous checks were not sufficient, so copy the file locally.
            PutResult copy = await TryCopyAndPutAsync(operationContext, remote, UrgencyHint.Nominal, CopyReason.Pin, trace: false);
            if (copy)
            {
                if (!updateContentTracker)
                {
                    return DistributedPinResult.SynchronousCopy(locations.Count);
                }

                // Inform the content directory that we have the file.
                // We wait for this to complete, rather than doing it fire-and-forget, because another machine in the ring may need the pinned content immediately.
                BoolResult updated = await UpdateContentTrackerWithNewReplicaAsync(operationContext, new[] { new ContentHashWithSize(remote.ContentHash, copy.ContentSize) }, UrgencyHint.Nominal);
                if (updated.Succeeded)
                {
                    return DistributedPinResult.SynchronousCopy(locations.Count);
                }
                else
                {
                    // Tracing the error separately.
                    Tracer.Warning(operationContext, $"Pin failed for hash {remote.ContentHash.ToShortString()}: local copy succeeded, but could not inform content directory due to {updated.ErrorMessage}.");
                    return new DistributedPinResult(locations.Count, updated);
                }
            }
            else
            {
                // Tracing the error separately.
                Tracer.Warning(operationContext, $"Pin failed for hash {remote.ContentHash.ToShortString()}: local copy failed with {copy}.");
                return DistributedPinResult.ContentNotFound(locations.Count);
            }
        }

        private async Task<BoolResult> TryCopyAndPutAndUpdateContentTrackerAsync(
            OperationContext operationContext,
            ContentHashWithSizeAndLocations remote,
            bool updateContentTracker,
            CopyReason reason)
        {
            PutResult copy = await TryCopyAndPutAsync(operationContext, remote, UrgencyHint.Nominal, reason, trace: true);
            if (copy && updateContentTracker)
            {
                return await UpdateContentTrackerWithNewReplicaAsync(operationContext, new[] { new ContentHashWithSize(remote.ContentHash, copy.ContentSize) }, UrgencyHint.Nominal);
            }

            return copy;
        }

        private Task UpdateContentTrackerWithLocalHitsAsync(OperationContext context, IReadOnlyList<ContentHashWithSizeAndLastAccessTime> contentHashesWithInfo, UrgencyHint urgencyHint)
        {
            if (Disposed)
            {
                // Nothing to do.
                return BoolTask.True;
            }

            if (contentHashesWithInfo.Count == 0)
            {
                // Nothing to do.
                return BoolTask.True;
            }


            IReadOnlyList<ContentHashWithSize> hashesToEagerUpdate = contentHashesWithInfo.Select(x => new ContentHashWithSize(x.Hash, x.Size)).ToList();

            // Wait for update to complete on remaining hashes to cover case where the record has expired and another machine in the ring requests it immediately after this pin succeeds.
            return UpdateContentTrackerWithNewReplicaAsync(context, hashesToEagerUpdate, urgencyHint);
        }

        internal async Task<IReadOnlyList<ContentHashWithSizeAndLocations>> GetLocationsForProactiveCopyAsync(
            OperationContext context,
            IReadOnlyList<ContentHash> hashes)
        {
            var originalLength = hashes.Count;
            if (_buildIdHash.HasValue)
            {
                // Add build id hash to hashes so build ring machines can be updated
                hashes = hashes.AppendItem(_buildIdHash.Value).ToList();
            }

            var result = await MultiLevelUtilities.RunMultiLevelWithMergeAsync(
                hashes,
                inputs => ContentLocationStore.GetBulkAsync(context, inputs, context.Token, UrgencyHint.Nominal, GetBulkOrigin.Local).ThrowIfFailureAsync(g => g.ContentHashesInfo),
                inputs => ContentLocationStore.GetBulkAsync(context, inputs, context.Token, UrgencyHint.Nominal, GetBulkOrigin.Global).ThrowIfFailureAsync(g => g.ContentHashesInfo),
                mergeResults: ContentHashWithSizeAndLocations.Merge,
                useFirstLevelResult: result =>
                {
                    if (result.Locations?.Count >= Settings.ProactiveCopyLocationsThreshold)
                    {
                        SessionCounters[Counters.GetLocationsSatisfiedFromLocal].Increment();
                        return true;
                    }
                    else
                    {
                        SessionCounters[Counters.GetLocationsSatisfiedFromRemote].Increment();
                        return false;
                    }
                });

            if (_buildIdHash.HasValue)
            {
                // Update build ring machines with retrieved locations
                _buildRingMachines = result.Last().Locations?.AppendItem(LocalCacheRootMachineLocation).ToArray() ?? CollectionUtilities.EmptyArray<MachineLocation>();
                return result.Take(originalLength).ToList();
            }
            else
            {
                return result;
            }
        }

        internal async Task<ProactiveCopyResult> ProactiveCopyIfNeededAsync(
            OperationContext context,
            ContentHash hash,
            bool tryBuildRing,
            CopyReason reason)
        {
            ContentHashWithSizeAndLocations result = await _proactiveCopyGetBulkNagleQueue.EnqueueAsync(hash);
            return await ProactiveCopyIfNeededAsync(context, result, tryBuildRing, reason);
        }

        internal Task<ProactiveCopyResult> ProactiveCopyIfNeededAsync(
            OperationContext context, 
            ContentHashWithSizeAndLocations info, 
            bool tryBuildRing, 
            CopyReason reason)
        {
            var hash = info.ContentHash;
            if (!_pendingProactivePuts.Add(hash)
                || info.ContentHash.IsEmptyHash()) // No reason to push an empty hash to another machine.
            {
                return Task.FromResult(ProactiveCopyResult.CopyNotRequiredResult);
            }

            // Don't trace this case since it would add too much log traffic.
            var replicatedLocations = info.Locations ?? CollectionUtilities.EmptyArray<MachineLocation>();
            if (replicatedLocations.Count >= Settings.ProactiveCopyLocationsThreshold)
            {
                SessionCounters[Counters.ProactiveCopiesSkipped].Increment();
                return Task.FromResult(ProactiveCopyResult.CopyNotRequiredResult);
            }

            return context.PerformOperationAsync(
                Tracer,
                traceErrorsOnly: !Settings.TraceProactiveCopy,
                operation: async () =>
                {
                    try
                    {
                        var insideRingCopyTask = ProactiveCopyInsideBuildRingAsync(context, hash, tryBuildRing, replicatedLocations, reason);

                        var outsideRingCopyTask = ProactiveCopyOutsideBuildRingAsync(context, hash, replicatedLocations, reason);

                        await Task.WhenAll(insideRingCopyTask, outsideRingCopyTask);

                        var insideRingResult = await insideRingCopyTask;
                        var outsideRingResult = await outsideRingCopyTask;

                        var retries = 0;
                        if (!insideRingResult.Succeeded)
                        {
                            while (outsideRingResult.Status.QualifiesForRetry() && retries < Settings.ProactiveCopyMaxRetries)
                            {
                                SessionCounters[Counters.ProactiveCopyRetries].Increment();
                                retries++;
                                outsideRingResult = await ProactiveCopyOutsideBuildRingAsync(context, hash, replicatedLocations, reason);
                            }
                        }

                        return new ProactiveCopyResult(insideRingResult, outsideRingResult, retries, info.Entry);
                    }
                    finally
                    {
                        _pendingProactivePuts.Remove(hash);
                    }
                },
                extraEndMessage: r => $"Hash={info.ContentHash}, Retries={r.Retries}, Reason=[{reason}]");
        }

        private Task<PushFileResult> ProactiveCopyOutsideBuildRingAsync(
            OperationContext context,
            ContentHash hash,
            IReadOnlyList<MachineLocation> replicatedLocations,
            CopyReason reason)
        {
            if ((Settings.ProactiveCopyMode & ProactiveCopyMode.OutsideRing) != 0)
            {
                Result<MachineLocation>? getLocationResult = null;
                var source = ProactiveCopyLocationSource.Random;

                // Try to select one of the designated machines for this hash.
                if (Settings.ProactiveCopyUsePreferredLocations && getLocationResult?.Succeeded != true)
                {
                    var designatedLocationsResult = ContentLocationStore.GetDesignatedLocations(hash);
                    if (designatedLocationsResult)
                    {
                        var candidates = designatedLocationsResult.Value
                            .Except(replicatedLocations).ToArray();

                        if (candidates.Length > 0)
                        {
                            getLocationResult = candidates[ThreadSafeRandom.Generator.Next(0, candidates.Length)];
                            source = ProactiveCopyLocationSource.DesignatedLocation;
                            SessionCounters[Counters.ProactiveCopy_OutsideRingFromPreferredLocations].Increment();
                        }
                    }
                }

                // Try to select one machine at random.
                if (getLocationResult?.Succeeded != true)
                {
                    // Make sure that the machine is not in the build ring and does not already have the content.
                    var machinesToSkip = replicatedLocations.Concat(_buildRingMachines).ToArray();
                    getLocationResult = ContentLocationStore.GetRandomMachineLocation(except: machinesToSkip);
                    source = ProactiveCopyLocationSource.Random;
                }

                if (getLocationResult.Succeeded)
                {
                    var candidate = getLocationResult.Value;
                    return PushContentAsync(context, hash, candidate, isInsideRing: false, reason, source);
                }
                else
                {
                    return Task.FromResult(new PushFileResult(getLocationResult));
                }
            }
            else
            {
                return Task.FromResult(PushFileResult.Disabled());
            }
        }

        private Task<PushFileResult> ProactiveCopyInsideBuildRingAsync(
            OperationContext context,
            ContentHash hash,
            bool tryBuildRing,
            IReadOnlyList<MachineLocation> replicatedLocations,
            CopyReason reason)
        {
            // Get random machine inside build ring
            if (tryBuildRing && (Settings.ProactiveCopyMode & ProactiveCopyMode.InsideRing) != 0)
            {
                SessionCounters[Counters.ProactiveCopy_InsideRingCopies].Increment();

                if (_buildIdHash != null)
                {
                    var candidates = _buildRingMachines
                        .Where(m => !m.Equals(LocalCacheRootMachineLocation))
                        .Where(m => ContentLocationStore.IsMachineActive(m)).ToArray();

                    if (candidates.Length > 0)
                    {
                        candidates = candidates.Except(replicatedLocations).ToArray();
                        if (candidates.Length > 0)
                        {
                            var candidate = candidates[ThreadSafeRandom.Generator.Next(0, candidates.Length)];
                            return PushContentAsync(context, hash, candidate, isInsideRing: true, reason, ProactiveCopyLocationSource.Random);
                        }
                        else
                        {
                            SessionCounters[Counters.ProactiveCopy_InsideRingFullyReplicated].Increment();
                            return Task.FromResult(new PushFileResult($"All candidates in the build ring for build {_buildId} already have the content."));
                        }
                    }
                    else
                    {
                        return Task.FromResult(new PushFileResult($"Could not find any machines belonging to the build ring for build {_buildId}."));
                    }
                }
                else
                {
                    return Task.FromResult(new PushFileResult("BuildId was not specified, so machines in the build ring cannot be found."));
                }
            }
            else
            {
                return Task.FromResult(PushFileResult.Disabled());
            }
        }

        private async Task<PushFileResult> PushContentAsync(OperationContext context, ContentHash hash, MachineLocation target, bool isInsideRing, CopyReason reason, ProactiveCopyLocationSource source)
        {
            if (Settings.PushProactiveCopies)
            {
                // It is possible that this method is used during proactive replication
                // and the hash was already evicted at the time this method is called.
                var streamResult = await Inner.OpenStreamAsync(context, hash, context.Token);
                if (!streamResult.Succeeded)
                {
                    return PushFileResult.SkipContentUnavailable();
                }

                using var stream = streamResult.Stream!;

                return await DistributedCopier.PushFileAsync(
                    context,
                    hash,
                    target,
                    stream,
                    isInsideRing,
                    reason,
                    source);
            }
            else
            {
                var requestResult = await DistributedCopier.RequestCopyFileAsync(context, hash, target, isInsideRing);
                if (requestResult)
                {
                    return PushFileResult.PushSucceeded();
                }

                return new PushFileResult(requestResult, "Failed requesting a copy");
            }
        }

        /// <inheritdoc />
        protected override CounterSet GetCounters() =>
            base.GetCounters()
                .Merge(DistributedCopier.GetCounters())
                .Merge(SessionCounters.ToCounterSet());
    }
}
