// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.Host.Service.Internal
{
    // TODO: move it to the library?
    public class MultiplexedContentStore : StartupShutdownBase, IContentStore, IRepairStore, IStreamStore, ICopyRequestHandler, IPushFileHandler, ILocalContentStore
    {
        /// <nodoc />
        public Dictionary<string, IContentStore> DrivesWithContentStore { get; }

        public string PreferredCacheDrive { get; }

        private ContentStoreTracer StoreTracer { get; } = new ContentStoreTracer(nameof(MultiplexedContentStore));

        public IContentStore PreferredContentStore { get; }

        /// <inheritdoc />
        protected override Tracer Tracer => StoreTracer;

        public MultiplexedContentStore(Dictionary<string, IContentStore> drivesWithContentStore, string preferredCacheDrive)
        {
            Contract.Requires(!string.IsNullOrEmpty(preferredCacheDrive), "preferredCacheDrive should not be null or empty.");
            Contract.Requires(drivesWithContentStore?.Count > 0, "drivesWithContentStore should not be null or empty.");
            Contract.Check(drivesWithContentStore.ContainsKey(preferredCacheDrive))?.Requires($"drivesWithContentStore should contain '{preferredCacheDrive}'.");
            DrivesWithContentStore = drivesWithContentStore;
            PreferredCacheDrive = preferredCacheDrive;
            PreferredContentStore = drivesWithContentStore[preferredCacheDrive];
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var finalResult = BoolResult.Success;

            var stores = DrivesWithContentStore.Values.ToArray();
            for (var i = 0; i < stores.Length; i++)
            {
                var startupResult = await stores[i].StartupAsync(context).ConfigureAwait(false);

                if (!startupResult.Succeeded)
                {
                    finalResult = startupResult;
                    for (var j = 0; j < i; j++)
                    {
                        var shutdownResult = await stores[j].ShutdownAsync(context).ConfigureAwait(false);
                        if (!shutdownResult.Succeeded)
                        {
                            finalResult = new BoolResult(finalResult, shutdownResult.ErrorMessage);
                        }
                    }
                }
            }

            return finalResult;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var finalResult = BoolResult.Success;

            foreach (var store in DrivesWithContentStore.Values)
            {
                if (store.StartupCompleted && !store.ShutdownCompleted)
                {
                    // Shutdown is available only when the store started up successfully and wasn't shut down yet.
                    var result = await store.ShutdownAsync(context).ConfigureAwait(false);
                    if (!result)
                    {
                        finalResult &= result;
                    }
                }
            }

            return finalResult;
        }

        protected override void DisposeCore()
        {
            foreach (IContentStore store in DrivesWithContentStore.Values)
            {
                store.Dispose();
            }
        }

        /// <nodoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(StoreTracer, new OperationContext(context), name, () =>
            {
                var sessions = new Dictionary<string, IReadOnlyContentSession>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, IContentStore> entry in DrivesWithContentStore)
                {
                    var result = entry.Value.CreateReadOnlySession(context, name, implicitPin);
                    if (!result.Succeeded)
                    {
                        foreach (var session in sessions.Values)
                        {
                            session.Dispose();
                        }

                        return new CreateSessionResult<IReadOnlyContentSession>(result);
                    }
                    sessions.Add(entry.Key, result.Session);
                }

                var multiCacheSession = new MultiplexedReadOnlyContentSession(sessions, name, this);
                return new CreateSessionResult<IReadOnlyContentSession>(multiCacheSession);
            });
        }

        /// <nodoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(StoreTracer, new OperationContext(context), name, () =>
            {
                var sessions = new Dictionary<string, IReadOnlyContentSession>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, IContentStore> entry in DrivesWithContentStore)
                {
                    var result = entry.Value.CreateSession(context, name, implicitPin);
                    if (!result.Succeeded)
                    {
                        foreach (var session in sessions.Values)
                        {
                            session.Dispose();
                        }

                        return new CreateSessionResult<IContentSession>(result);
                    }
                    sessions.Add(entry.Key, result.Session);
                }

                var multiCacheSession = new MultiplexedContentSession(sessions, name, this);
                return new CreateSessionResult<IContentSession>(multiCacheSession);
            });
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<ContentStoreTracer>.RunAsync(
               StoreTracer,
               new OperationContext(context),
               async () =>
               {
                   CounterSet aggregatedCounters = new CounterSet();
                   foreach (var kvp in DrivesWithContentStore)
                   {
                       var stats = await kvp.Value.GetStatsAsync(context);
                       if (stats.Succeeded)
                       {
                           aggregatedCounters.Merge(stats.CounterSet, $"{kvp.Value.GetType().Name}.{kvp.Key}.");
                       }
                   }

                   return new GetStatsResult(aggregatedCounters);
               });
        }

        /// <inheritdoc />
        public async Task<BoolResult> RemoveFromTrackerAsync(Context context)
        {
            using (var operationContext = TrackShutdown(context, CancellationToken.None))
            {
                return await operationContext.Context.PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        var removeTaskByStore = new Dictionary<string, Task<BoolResult>>();

                        foreach (var kvp in DrivesWithContentStore)
                        {
                            if (kvp.Value is IRepairStore store)
                            {
                                removeTaskByStore.Add(kvp.Key, store.RemoveFromTrackerAsync(context));
                            }
                        }

                        await Task.WhenAll(removeTaskByStore.Values);

                        var sb = new StringBuilder();
                        foreach (var kvp in removeTaskByStore)
                        {
                            var removeFromTrackerResult = await kvp.Value;
                            if (!removeFromTrackerResult.Succeeded)
                            {
                                sb.Concat($"{kvp.Key} repair handling failed, error=[{removeFromTrackerResult}]", "; ");
                            }
                        }

                        if (sb.Length > 0)
                        {
                            return new BoolResult(sb.ToString());
                        }
                        else
                        {
                            return BoolResult.Success;
                        }
                    });
            }
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            return PerformStoreOperationAsync<IStreamStore, OpenStreamResult>(store => store.StreamContentAsync(context, contentHash));
        }

        /// <inheritdoc />
        public Task<BoolResult> HandleCopyFileRequestAsync(Context context, ContentHash hash, CancellationToken token)
        {
            return PerformStoreOperationAsync<ICopyRequestHandler, BoolResult>(store => store.HandleCopyFileRequestAsync(context, hash, token));
        }

        private async Task<TResult> PerformStoreOperationAsync<TStore, TResult>(Func<TStore, Task<TResult>> executeAsync, AbsolutePath path = null)
            where TResult : ResultBase
        {
            TResult result = null;

            foreach (var store in GetStoresInOrder<TStore>(path))
            {
                result = await executeAsync(store);

                if (result.Succeeded)
                {
                    return result;
                }
            }

            return result ?? new ErrorResult($"Could not find a content store which implements {typeof(TStore).Name} in {nameof(MultiplexedContentStore)}.").AsResult<TResult>();
        }

        private bool PerformStoreOperation<TStore>(Func<TStore, bool> executeAsync)
        {
            var result = false;

            foreach (var store in GetStoresInOrder<TStore>())
            {
                result = executeAsync(store);

                if (result)
                {
                    return result;
                }
            }

            return result;
        }

        private IEnumerable<TStore> GetStoresInOrder<TStore>(AbsolutePath path = null)
        {
            var pathPreferredDrive = string.Empty;
            if (path != null)
            {
                pathPreferredDrive = path.GetPathRoot();
                if (!StringComparer.OrdinalIgnoreCase.Equals(PreferredCacheDrive, pathPreferredDrive)
                    && DrivesWithContentStore.TryGetValue(pathPreferredDrive, out var pathPreferredStore)
                    && pathPreferredStore is TStore preferredTStore)
                {
                    yield return preferredTStore;
                }
            }

            if (DrivesWithContentStore[PreferredCacheDrive] is TStore store)
            {
                yield return store;
            }

            foreach (var kvp in DrivesWithContentStore)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(PreferredCacheDrive, kvp.Key)
                    || StringComparer.OrdinalIgnoreCase.Equals(pathPreferredDrive, kvp.Key))
                {
                    // Already yielded the preferred cache
                    continue;
                }

                if (kvp.Value is TStore otherStore)
                {
                    yield return otherStore;
                }
            }
        }

        /// <inheritdoc />
        public async Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions)
        {
            long contentSize = 0L;
            if (!deleteOptions.DeleteLocalOnly)
            {
                var mapping = new Dictionary<string, DeleteResult>();
                foreach (var kvp in DrivesWithContentStore)
                {
                    var deleteResult = await kvp.Value.DeleteAsync(context, contentHash, deleteOptions);
                    if (deleteResult is DistributedDeleteResult distributedDelete)
                    {
                        foreach (var pair in distributedDelete.DeleteMapping)
                        {
                            mapping.Add(pair.Key, pair.Value);
                        }
                    }

                    contentSize = Math.Max(deleteResult.ContentSize, contentSize);
                }

                return new DistributedDeleteResult(contentHash, contentSize, mapping);
            }

            int code = (int)DeleteResult.ResultCode.ContentNotFound;

            foreach (var kvp in DrivesWithContentStore)
            {
                var deleteResult = await kvp.Value.DeleteAsync(context, contentHash, deleteOptions);
                if (deleteResult.Succeeded)
                {
                    code = Math.Max(code, (int)deleteResult.Code);
                    contentSize = Math.Max(deleteResult.ContentSize, contentSize);
                }
                else
                {
                    return deleteResult;
                }
            }

            return new DeleteResult((DeleteResult.ResultCode)code, contentHash, contentSize);
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            foreach (var kvp in DrivesWithContentStore)
            {
                kvp.Value.PostInitializationCompleted(context, result);
            }
        }

        /// <inheritdoc />
        public Task<PutResult> HandlePushFileAsync(Context context, ContentHash hash, FileSource source, CancellationToken token)
        {
            AbsolutePath preferredPath = null;
            if (source.FileRealizationMode == FileRealizationMode.Move || source.FileRealizationMode == FileRealizationMode.HardLink)
            {
                // If the store is in a different drive than the file source, we should try to put into the same drive
                // first, given that it's faster than actually copying to a different drive.
                preferredPath = source.Path;
            }

            return PerformStoreOperationAsync<IPushFileHandler, PutResult>(store => store.HandlePushFileAsync(context, hash, source, token), preferredPath);
        }

        /// <inheritdoc />
        public bool CanAcceptContent(Context context, ContentHash hash, out RejectionReason rejectionReason)
        {
            // Rejection reason will be whatever we called last, or NotSupported if no stores implement IPushFileHandler.
            rejectionReason = RejectionReason.NotSupported;

            foreach (var store in GetStoresInOrder<IPushFileHandler>())
            {
                if (store.CanAcceptContent(context, hash, out rejectionReason))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<ContentInfo>> GetContentInfoAsync(CancellationToken token)
        {
            var contentInfos = Enumerable.Empty<ContentInfo>();

            foreach (var store in GetStoresInOrder<ILocalContentStore>())
            {
                contentInfos = contentInfos.Concat(await store.GetContentInfoAsync(token));
            }

            return contentInfos;
        }

        /// <inheritdoc />
        public bool Contains(ContentHash hash)
        {
            foreach (var store in GetStoresInOrder<ILocalContentStore>())
            {
                if (store.Contains(hash))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public bool TryGetContentInfo(ContentHash hash, out ContentInfo info)
        {
            foreach (var store in GetStoresInOrder<ILocalContentStore>())
            {
                if (store.TryGetContentInfo(hash, out info))
                {
                    return true;
                }
            }

            info = default;
            return false;
        }

        /// <inheritdoc />
        public void UpdateLastAccessTimeIfNewer(ContentHash hash, DateTime newLastAccessTime)
        {
            foreach (var store in GetStoresInOrder<ILocalContentStore>())
            {
                store.UpdateLastAccessTimeIfNewer(hash, newLastAccessTime);
            }
        }
    }
}
