// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
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
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// A store that is based on content locations for opaque file locations.
    /// </summary>
    public class DistributedContentStore : StartupShutdownBase, IContentStore, IRepairStore, IDistributedLocationStore, IStreamStore, ICopyRequestHandler, IPushFileHandler, IDeleteFileHandler, IDistributedContentCopierHost
    {
        // Used for testing.
        internal enum Counters
        {
            ProactiveReplication_Succeeded,
            ProactiveReplication_Failed,
            ProactiveReplication_Skipped,
            ProactiveReplication_Rejected,
            RejectedPushCopyCount_OlderThanEvicted,
            ProactiveReplication
        }

        internal readonly CounterCollection<Counters> CounterCollection = new CounterCollection<Counters>();

        /// <summary>
        /// The location of the local cache root
        /// </summary>
        public MachineLocation LocalMachineLocation { get; }

        internal IContentLocationStoreFactory ContentLocationStoreFactory { get; }
        private readonly ContentStoreTracer _tracer = new ContentStoreTracer(nameof(DistributedContentStore));
        private readonly IClock _clock;

        private DateTime? _lastEvictedEffectiveLastAccessTime;

        /// <summary>
        /// Flag for testing using local Redis instance.
        /// </summary>
        internal bool DisposeContentLocationStoreFactory = true;

        internal IContentStore InnerContentStore { get; }

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        private IContentLocationStore? _contentLocationStore;

        public ColdStorage? ColdStorage { get; }

        internal IContentLocationStore ContentLocationStore => NotNull(_contentLocationStore, nameof(_contentLocationStore));

        private readonly DistributedContentStoreSettings _settings;

        /// <summary>
        /// Task source that is set to completion state when the system is fully initialized.
        /// The main goal of this field is to avoid the race condition when eviction is triggered during startup
        /// when hibernated sessions are not fully reloaded.
        /// </summary>
        private readonly TaskSourceSlim<BoolResult> _postInitializationCompletion = TaskSourceSlim.Create<BoolResult>();

        private readonly DistributedContentCopier _distributedCopier;
        private readonly DisposableDirectory _copierWorkingDirectory;
        private Lazy<Task<Result<ReadOnlyDistributedContentSession>>>? _proactiveCopySession;
        internal Lazy<Task<Result<ReadOnlyDistributedContentSession>>> ProactiveCopySession => NotNull(_proactiveCopySession, nameof(_proactiveCopySession));

        /// <nodoc />
        public DistributedContentStore(
            MachineLocation localMachineLocation,
            AbsolutePath localCacheRoot,
            Func<IDistributedLocationStore, IContentStore> innerContentStoreFunc,
            IContentLocationStoreFactory contentLocationStoreFactory,
            DistributedContentStoreSettings settings,
            DistributedContentCopier distributedCopier,
            ColdStorage coldStorage,
            IClock? clock = null)
        {
            Contract.Requires(settings != null);

            LocalMachineLocation = localMachineLocation;
            ContentLocationStoreFactory = contentLocationStoreFactory;
            _clock = clock ?? SystemClock.Instance;
            _distributedCopier = distributedCopier;
            _copierWorkingDirectory = new DisposableDirectory(distributedCopier.FileSystem, localCacheRoot / "Temp");

            _settings = settings;

            ColdStorage = coldStorage;

            InnerContentStore = innerContentStoreFunc(this);
        }

        [return: NotNull]
        private static T NotNull<T>([MaybeNull] T value, string memberName)
        {
            Contract.Check(value != null)?.Assert($"{memberName} is null. Did you forget to call StartupAsync?");
            return value;
        }

        AbsolutePath IDistributedContentCopierHost.WorkingFolder => _copierWorkingDirectory.Path;

        void IDistributedContentCopierHost.ReportReputation(MachineLocation location, MachineReputation reputation)
        {
            ContentLocationStore.MachineReputationTracker.ReportReputation(location, reputation);
        }

        private Task<Result<ReadOnlyDistributedContentSession>> CreateCopySession(Context context)
        {
            var sessionId = Guid.NewGuid().ToString();

            var operationContext = OperationContext(context.CreateNested(componentName: nameof(DistributedContentStore)));
            return operationContext.PerformOperationAsync(_tracer,
                async () =>
                {
                    // NOTE: We use ImplicitPin.None so that the OpenStream calls triggered by RequestCopy will only pull the content, NOT pin it in the local store.
                    var sessionResult = CreateReadOnlySession(operationContext, $"{sessionId}-DefaultCopy", ImplicitPin.None).ThrowIfFailure();
                    var session = sessionResult.Session!;

                    await session.StartupAsync(context).ThrowIfFailure();
                    return Result.Success((ReadOnlyDistributedContentSession)session);
                });
        }

        /// <inheritdoc />
        public override Task<BoolResult> StartupAsync(Context context)
        {
            var startupTask = base.StartupAsync(context);

            _proactiveCopySession = new Lazy<Task<Result<ReadOnlyDistributedContentSession>>>(() => CreateCopySession(context));

            if (_settings.SetPostInitializationCompletionAfterStartup)
            {
                _tracer.Debug(context, "Linking post-initialization completion task with the result of StartupAsync.");
                _postInitializationCompletion.LinkToTask(startupTask);
            }

            return startupTask;
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            _tracer.Debug(context, $"Setting result for post-initialization completion task to '{result}'.");
            _postInitializationCompletion.TrySetResult(result);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _distributedCopier.StartupAsync(context).ThrowIfFailure();

            // Initializing inner store before initializing LocalLocationStore because LocalLocationStore may use inner
            // store for reconciliation purposes
            await InnerContentStore.StartupAsync(context).ThrowIfFailure();

            // NOTE: We initialize the inner content store before the factory because it is possible for a machine's
            // drive to have no quota leftover, in which case LLS will fail to start. If the drive is full and eviction
            // needs to happen, then it'll do so without updating LLS. The expectation is that the reconciliation will
            // fix these cases over time.
            await ContentLocationStoreFactory.StartupAsync(context).ThrowIfFailure();

            _contentLocationStore = await ContentLocationStoreFactory.CreateAsync(LocalMachineLocation, InnerContentStore as ILocalContentStore);

            await _contentLocationStore.StartupAsync(context).ThrowIfFailure();

            if (_settings.EnableProactiveReplication
                && _contentLocationStore is TransitioningContentLocationStore tcs
                && InnerContentStore is ILocalContentStore localContentStore)
            {
                await ProactiveReplicationAsync(context.CreateNested(nameof(DistributedContentStore)), localContentStore, tcs).ThrowIfFailure();
            }

            return BoolResult.Success;
        }

        private Task<BoolResult> ProactiveReplicationAsync(
            OperationContext context,
            ILocalContentStore localContentStore,
            TransitioningContentLocationStore contentLocationStore)
        {
            return context.PerformOperationAsync(
                   Tracer,
                   async () =>
                   {
                       var proactiveCopySession = await ProactiveCopySession.Value.ThrowIfFailureAsync();

                       await contentLocationStore.LocalLocationStore.EnsureInitializedAsync().ThrowIfFailure();

                       while (!context.Token.IsCancellationRequested)
                       {
                           // Create task before starting operation to ensure uniform intervals assuming operation takes less than the delay.
                           var delayTask = Task.Delay(_settings.ProactiveReplicationInterval, context.Token);

                           await ProactiveReplicationIterationAsync(context, proactiveCopySession, localContentStore, contentLocationStore).ThrowIfFailure();

                           if (_settings.InlineOperationsForTests)
                           {
                               // Inlining is used only for testing purposes. In those cases,
                               // we only perform one proactive replication.
                               break;
                           }

                           await delayTask;
                       }

                       return BoolResult.Success;
                   })
                .FireAndForgetOrInlineAsync(context, _settings.InlineOperationsForTests);
        }

        private Task<ProactiveReplicationResult> ProactiveReplicationIterationAsync(
            OperationContext context,
            ReadOnlyDistributedContentSession proactiveCopySession,
            ILocalContentStore localContentStore,
            TransitioningContentLocationStore contentLocationStore)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // Important to yield as GetContentInfoAsync has a synchronous implementation.
                    await Task.Yield();

                    var localContent = (await localContentStore.GetContentInfoAsync(context.Token))
                        .OrderByDescending(info => info.LastAccessTimeUtc) // GetHashesInEvictionOrder expects entries to already be ordered by last access time.
                        .Select(info => new ContentHashWithLastAccessTimeAndReplicaCount(info.ContentHash, info.LastAccessTimeUtc))
                        .ToArray();

                    var contents = contentLocationStore.GetHashesInEvictionOrder(context, localContent, reverse: true);

                    var succeeded = 0;
                    var failed = 0;
                    var skipped = 0;
                    var scanned = 0;
                    var rejected = 0;
                    var delayTask = Task.CompletedTask;
                    var wasPreviousCopyNeeded = true;
                    ContentEvictionInfo? lastVisited = default;

                    IEnumerable<ContentEvictionInfo> getReplicableHashes()
                    {
                        foreach (var content in contents)
                        {
                            scanned++;

                            if (content.ReplicaCount < _settings.ProactiveCopyLocationsThreshold)
                            {
                                yield return content;
                            }
                            else
                            {
                                CounterCollection[Counters.ProactiveReplication_Skipped].Increment();
                                skipped++;
                            }
                        }
                    }

                    foreach (var page in getReplicableHashes().GetPages(_settings.ProactiveCopyGetBulkBatchSize))
                    {
                        var contentInfos = await proactiveCopySession.GetLocationsForProactiveCopyAsync(context, page.SelectList(c => c.ContentHash));
                        for (int i = 0; i < contentInfos.Count; i++)
                        {
                            context.Token.ThrowIfCancellationRequested();

                            var contentInfo = contentInfos[i];
                            lastVisited = page[i];

                            if (wasPreviousCopyNeeded)
                            {
                                await delayTask;
                                delayTask = Task.Delay(_settings.DelayForProactiveReplication, context.Token);
                            }

                            var result = await proactiveCopySession.ProactiveCopyIfNeededAsync(
                                context,
                                contentInfo,
                                tryBuildRing: false,
                                reason: CopyReason.ProactiveBackground);

                            wasPreviousCopyNeeded = true;
                            switch (result.Status)
                            {
                                case ProactiveCopyStatus.Success:
                                    CounterCollection[Counters.ProactiveReplication_Succeeded].Increment();
                                    succeeded++;
                                    break;
                                case ProactiveCopyStatus.Skipped:
                                    CounterCollection[Counters.ProactiveReplication_Skipped].Increment();
                                    skipped++;
                                    wasPreviousCopyNeeded = false;
                                    break;
                                case ProactiveCopyStatus.Rejected:
                                    rejected++;
                                    CounterCollection[Counters.ProactiveReplication_Rejected].Increment();
                                    break;
                                case ProactiveCopyStatus.Error:
                                    CounterCollection[Counters.ProactiveReplication_Failed].Increment();
                                    failed++;
                                    break;
                            }

                            if ((succeeded + failed) >= _settings.ProactiveReplicationCopyLimit)
                            {
                                break;
                            }
                        }

                        if ((succeeded + failed) >= _settings.ProactiveReplicationCopyLimit)
                        {
                            break;
                        }
                    }

                    return new ProactiveReplicationResult(succeeded, failed, skipped, rejected, localContent.Length, scanned, lastVisited);
                },
                counter: CounterCollection[Counters.ProactiveReplication]);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var results = new List<(string operation, BoolResult result)>();

            if (ProactiveCopySession.IsValueCreated)
            {
                var sessionResult = await ProactiveCopySession.Value;
                if (sessionResult.Succeeded)
                {
                    var proactiveCopySessionShutdownResult = await sessionResult.Value.ShutdownAsync(context);
                    results.Add((nameof(ProactiveCopySession), proactiveCopySessionShutdownResult));
                }
            }

            var innerResult = await InnerContentStore.ShutdownAsync(context);
            results.Add((nameof(InnerContentStore), innerResult));

            if (_contentLocationStore != null)
            {
                var locationStoreResult = await _contentLocationStore.ShutdownAsync(context);
                results.Add((nameof(_contentLocationStore), locationStoreResult));
            }

            var factoryResult = await ContentLocationStoreFactory.ShutdownAsync(context);
            results.Add((nameof(ContentLocationStoreFactory), factoryResult));

            _copierWorkingDirectory.Dispose();

            var copierResult = await _distributedCopier.ShutdownAsync(context);
            results.Add((nameof(_distributedCopier), copierResult));

            return ShutdownErrorCompiler(results);
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(_tracer, OperationContext(context), name, () =>
            {
                CreateSessionResult<IContentSession> innerSessionResult = InnerContentStore.CreateSession(context, name, implicitPin);

                if (innerSessionResult.Succeeded)
                {
                    var session = new ReadOnlyDistributedContentSession(
                            name,
                            innerSessionResult.Session,
                            ContentLocationStore,
                            _distributedCopier,
                            this,
                            LocalMachineLocation,
                            ColdStorage,
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
                    var session = new DistributedContentSession(
                            name,
                            innerSessionResult.Session,
                            _contentLocationStore,
                            _distributedCopier,
                            this,
                            LocalMachineLocation,
                            ColdStorage,
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

                    return new GetStatsResult(counterSet);
                }

                return result;
            });
        }

        /// <summary>
        /// Remove local location from the content tracker.
        /// </summary>
        public async Task<BoolResult> RemoveFromTrackerAsync(Context context)
        {
            if (_settings.EnableRepairHandling)
            {
                var result = await ContentLocationStore.InvalidateLocalMachineAsync(context, CancellationToken.None);
                if (!result)
                {
                    return result;
                }
            }

            return BoolResult.Success;
        }

        /// <summary>
        /// Determines if final BoolResult is success or error.
        /// </summary>
        private static BoolResult ShutdownErrorCompiler(IReadOnlyList<(string operation, BoolResult result)> results)
        {
            var sb = new StringBuilder();
            foreach (var (operation, result) in results)
            {
                if (!result)
                {
                    // TODO: Consider compiling Item2's Diagnostics into the final result's Diagnostics instead of ErrorMessage (bug 1365340)
                    sb.Append($"{operation}: {result} ");
                }
            }

            return sb.Length != 0 ? new BoolResult(sb.ToString()) : BoolResult.Success;
        }

        /// <nodoc />
        protected override void DisposeCore()
        {
            InnerContentStore.Dispose();

            if (DisposeContentLocationStoreFactory)
            {
                ContentLocationStoreFactory.Dispose();
            }
        }

        /// <nodoc />
        public bool CanComputeLru => (_contentLocationStore as IDistributedLocationStore)?.CanComputeLru ?? false;

        /// <nodoc />
        public Task<BoolResult> UnregisterAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken token, TimeSpan? minEffectiveAge = null)
        {
            if (InnerContentStore is ILocalContentStore localContentStore)
            {
                // Filter out hashes which exist in the local content store (may have been re-added by a recent put).
                var filteredHashes = contentHashes.Where(hash => !localContentStore.Contains(hash)).ToList();
                if (filteredHashes.Count != contentHashes.Count)
                {
                    Tracer.Debug(context, $"Hashes not unregistered because they are still present in local store: [{string.Join(",", contentHashes.Except(filteredHashes))}]");
                    contentHashes = filteredHashes;
                }
            }

            if (_settings.ProactiveCopyRejectOldContent && minEffectiveAge != null)
            {
                _lastEvictedEffectiveLastAccessTime = _clock.UtcNow - minEffectiveAge;
            }

            // This method as well as GetHashesInEvictionOrder maybe called
            // when the startup is not fully finished.
            // So we need to potentially wait here to make sure that the system is fully initialized to avoid contract violations.
            WaitForPostInitializationCompletionIfNeeded(context);

            return ContentLocationStore.TrimBulkAsync(context, contentHashes, token, UrgencyHint.Nominal);
        }

        /// <nodoc />
        public IEnumerable<ContentEvictionInfo> GetHashesInEvictionOrder(Context context, IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo)
        {
            // Ensure startup was called then wait for it to complete successfully (or error)
            // This logic is important to avoid runtime errors when, for instance, QuotaKeeper tries
            // to evict content right after startup and calls GetLruPages.
            Contract.Assert(StartupStarted);
            WaitForPostInitializationCompletionIfNeeded(context);

            Contract.Assert(_contentLocationStore is IDistributedLocationStore);
            if (_contentLocationStore is IDistributedLocationStore distributedStore)
            {
                return distributedStore.GetHashesInEvictionOrder(context, contentHashesWithInfo);
            }
            else
            {
                throw Contract.AssertFailure($"Cannot call GetLruPages when CanComputeLru returns false");
            }
        }

        private void WaitForPostInitializationCompletionIfNeeded(Context context)
        {
            var task = _postInitializationCompletion.Task;
            if (!task.IsCompleted)
            {
                var operationContext = new OperationContext(context);
                operationContext.PerformOperation(Tracer, () => waitForCompletion(), traceOperationStarted: false).ThrowIfFailure();
            }

            BoolResult waitForCompletion()
            {
                _tracer.Debug(context, $"Post-initialization is not done. Waiting for it to finish...");
                return task.GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Attempts to get local location store if enabled
        /// </summary>
        public bool TryGetLocalLocationStore([NotNullWhen(true)] out LocalLocationStore? localLocationStore)
        {
            if (_contentLocationStore is TransitioningContentLocationStore tcs)
            {
                localLocationStore = tcs.LocalLocationStore;
                return true;
            }

            localLocationStore = null;
            return false;
        }

        /// <summary>
        /// Gets the associated local location store instance
        /// </summary>
        public LocalLocationStore? LocalLocationStore => (_contentLocationStore as TransitioningContentLocationStore)?.LocalLocationStore;

        /// <summary>
        /// Checks the LLS <see cref="DistributedCentralStorage"/> for the content if available and returns
        /// the storage instance if content is found
        /// </summary>
        private bool CheckLlsForContent(ContentHash desiredContent, [NotNullWhen(true)] out DistributedCentralStorage? storage)
        {
            if (_contentLocationStore is TransitioningContentLocationStore tcs
                && tcs.LocalLocationStore.DistributedCentralStorage != null
                && tcs.LocalLocationStore.DistributedCentralStorage.HasContent(desiredContent))
            {
                storage = tcs.LocalLocationStore.DistributedCentralStorage;
                return true;
            }

            storage = default;
            return false;
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            // NOTE: Checking LLS for content needs to happen first since the query to the inner stream store result
            // is used even if the result is fails.
            if (CheckLlsForContent(contentHash, out var storage))
            {
                var result = await storage.StreamContentAsync(context, contentHash);
                if (result.Succeeded)
                {
                    return result;
                }
            }

            if (InnerContentStore is IStreamStore innerStreamStore)
            {
                return await innerStreamStore.StreamContentAsync(context, contentHash);
            }

            return new OpenStreamResult($"{InnerContentStore} does not implement {nameof(IStreamStore)} in {nameof(DistributedContentStore)}.");
        }

        Task<DeleteResult> IDeleteFileHandler.HandleDeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions) => DeleteAsync(context, contentHash, deleteOptions);

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
        {
            var operationContext = OperationContext(context);
            deleteOptions ??= new DeleteContentOptions() { DeleteLocalOnly = true };

            return operationContext.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var deleteResult = await InnerContentStore.DeleteAsync(context, contentHash, deleteOptions);
                    var contentHashes = new ContentHash[] { contentHash };
                    if (!deleteResult)
                    {
                        return deleteResult;
                    }

                    // Tell the event hub that this machine has removed the content locally
                    var unRegisterResult = await UnregisterAsync(context, contentHashes, operationContext.Token).ThrowIfFailure();
                    if (!unRegisterResult)
                    {
                        return new DeleteResult(unRegisterResult, unRegisterResult.ToString());
                    }

                    if (deleteOptions.DeleteLocalOnly)
                    {
                        return deleteResult;
                    }

                    var deleteResultsMapping = new Dictionary<string, DeleteResult>();

                    var result = await ContentLocationStore.GetBulkAsync(
                        context,
                        contentHashes,
                        operationContext.Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local);
                    if (!result)
                    {
                        deleteResult = new DeleteResult(result, result.ToString());
                        deleteResultsMapping.Add(LocalMachineLocation.Path, deleteResult);
                        return new DistributedDeleteResult(contentHash, deleteResult.ContentSize, deleteResultsMapping);
                    }

                    deleteResultsMapping.Add(LocalMachineLocation.Path, deleteResult);

                    // Go through each machine that has this content, and delete async locally on each machine.
                    var machineLocations = result.ContentHashesInfo[0].Locations;
                    if (machineLocations != null)
                    {
                        return await _distributedCopier.DeleteAsync(operationContext, contentHash, deleteResult.ContentSize, machineLocations, deleteResultsMapping);
                    }

                    return new DistributedDeleteResult(contentHash, deleteResult.ContentSize, deleteResultsMapping);
                });
        }

        /// <inheritdoc />
        public Task<BoolResult> HandleCopyFileRequestAsync(Context context, ContentHash hash, CancellationToken token)
        {
            using var shutdownTracker = TrackShutdown(context, token);
            var operationContext = shutdownTracker.Context;
            return operationContext.PerformOperationAsync(Tracer,
                async () =>
                {
                    var session = await ProactiveCopySession.Value.ThrowIfFailureAsync();
                    using (await session.OpenStreamAsync(context, hash, operationContext.Token).ThrowIfFailureAsync(o => o.Stream))
                    {
                        // Opening stream to ensure the content is copied locally. Stream is immediately disposed.
                    }

                    return BoolResult.Success;
                },
                traceOperationStarted: false,
                extraEndMessage: _ => $"Hash=[{hash.ToShortString()}]");
        }

        /// <inheritdoc />
        public async Task<PutResult> HandlePushFileAsync(Context context, ContentHash hash, FileSource source, CancellationToken token)
        {
            if (InnerContentStore is IPushFileHandler inner)
            {
                var result = await inner.HandlePushFileAsync(context, hash, source, token);
                if (!result)
                {
                    return result;
                }

                var registerResult = await ContentLocationStore.RegisterLocalLocationAsync(context, new[] { new ContentHashWithSize(hash, result.ContentSize) }, token, UrgencyHint.Nominal, touch: false);
                if (!registerResult)
                {
                    return new PutResult(registerResult);
                }

                return result;
            }

            return new PutResult(new InvalidOperationException($"{nameof(InnerContentStore)} does not implement {nameof(IPushFileHandler)}"), hash);
        }

        /// <inheritdoc />
        public bool CanAcceptContent(Context context, ContentHash hash, out RejectionReason rejectionReason)
        {
            if (InnerContentStore is IPushFileHandler inner)
            {
                if (!inner.CanAcceptContent(context, hash, out rejectionReason))
                {
                    return false;
                }
            }

            if (_settings.ProactiveCopyRejectOldContent)
            {
                var operationContext = OperationContext(context);
                if (TryGetLocalLocationStore(out var lls) && _contentLocationStore is TransitioningContentLocationStore tcs)
                {
                    if (lls.Database.TryGetEntry(operationContext, hash, out var entry))
                    {
                        var effectiveLastAccessTimeResult =
                            lls.GetEffectiveLastAccessTimes(operationContext, tcs, new ContentHashWithLastAccessTime[] { new ContentHashWithLastAccessTime(hash, entry.LastAccessTimeUtc.ToDateTime()) });
                        if (effectiveLastAccessTimeResult.Succeeded)
                        {
                            var effectiveAge = effectiveLastAccessTimeResult.Value[0].EffectiveAge;
                            var effectiveLastAccessTime = _clock.UtcNow - effectiveAge;
                            if (_lastEvictedEffectiveLastAccessTime > effectiveLastAccessTime == true)
                            {
                                CounterCollection[Counters.RejectedPushCopyCount_OlderThanEvicted].Increment();
                                rejectionReason = RejectionReason.OlderThanLastEvictedContent;
                                return false;
                            }
                        }
                    }
                }
            }

            rejectionReason = RejectionReason.Accepted;
            return true;
        }
    }
}
