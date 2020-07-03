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

namespace BuildXL.Cache.Host.Service.Internal
{
    // TODO: move it to the library?
    public class MultiplexedContentStore : IContentStore, IRepairStore, IStreamStore, ICopyRequestHandler, IPushFileHandler
    {
        private readonly Dictionary<string, IContentStore> _drivesWithContentStore;
        private readonly string _preferredCacheDrive;

        public IContentStore PreferredContentStore { get; }

        /// <summary>
        /// Execution tracer for the session.
        /// </summary>
        protected readonly ContentSessionTracer SessionTracer = new ContentSessionTracer(nameof(MultiplexedContentSession));

        /// <summary>
        /// Execution tracer for the readonly session.
        /// </summary>
        protected readonly ContentSessionTracer ReadOnlySessionTracer = new ContentSessionTracer(nameof(MultiplexedReadOnlyContentSession));

        /// <summary>
        ///     Execution tracer.
        /// </summary>
        protected readonly ContentStoreTracer Tracer = new ContentStoreTracer(nameof(MultiplexedContentStore));

        private bool _disposed;

        public MultiplexedContentStore(Dictionary<string, IContentStore> drivesWithContentStore, string preferredCacheDrive)
        {
            Contract.Requires(!string.IsNullOrEmpty(preferredCacheDrive), "preferredCacheDrive should not be null or empty.");
            Contract.Requires(drivesWithContentStore?.Count > 0, "drivesWithContentStore should not be null or empty.");
            Contract.Check(drivesWithContentStore.ContainsKey(preferredCacheDrive))?.Requires($"drivesWithContentStore should contain '{preferredCacheDrive}'.");
            _drivesWithContentStore = drivesWithContentStore;
            _preferredCacheDrive = preferredCacheDrive;
            PreferredContentStore = drivesWithContentStore[preferredCacheDrive];
        }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            return StartupCall<ContentStoreTracer>.RunAsync(Tracer, context, async () =>
            {
                StartupStarted = true;
                var finalResult = BoolResult.Success;

                var stores = _drivesWithContentStore.Values.ToArray();
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

                StartupCompleted = true;
                return finalResult;
            });
        }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            return ShutdownCall<ContentStoreTracer>.RunAsync(Tracer, context, async () =>
            {
                ShutdownStarted = true;
                var finalResult = BoolResult.Success;

                foreach (var store in _drivesWithContentStore.Values)
                {
                    if (store.StartupCompleted && !store.ShutdownCompleted)
                    {
                        // Shutdown is available only when the store started up successfully and wasn't shut down yet.
                        var result = await store.ShutdownAsync(context).ConfigureAwait(false);
                        if (!result)
                        {
                            finalResult = new BoolResult(finalResult, result.ErrorMessage);
                        }
                    }
                }

                ShutdownCompleted = true;
                return finalResult;
            });

        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                Dispose(true);
                _disposed = true;
            }
        }

        /// <summary>
        ///     Protected implementation of Dispose pattern.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (IContentStore store in _drivesWithContentStore.Values)
                {
                    store.Dispose();
                }
            }
        }

        /// <nodoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(Tracer, new OperationContext(context), name, () =>
            {
                var sessions = new Dictionary<string, IReadOnlyContentSession>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, IContentStore> entry in _drivesWithContentStore)
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

                var multiCacheSession = new MultiplexedReadOnlyContentSession(ReadOnlySessionTracer, sessions, name, _preferredCacheDrive);
                return new CreateSessionResult<IReadOnlyContentSession>(multiCacheSession);
            });
        }

        /// <nodoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(Tracer, new OperationContext(context), name, () =>
            {
                var sessions = new Dictionary<string, IReadOnlyContentSession>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, IContentStore> entry in _drivesWithContentStore)
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

                var multiCacheSession = new MultiplexedContentSession(SessionTracer, sessions, name, _preferredCacheDrive);
                return new CreateSessionResult<IContentSession>(multiCacheSession);
            });
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<ContentStoreTracer>.RunAsync(
               Tracer,
               new OperationContext(context),
               async () =>
               {
                   CounterSet aggregatedCounters = new CounterSet();
                   aggregatedCounters.Merge(SessionTracer.GetCounters(), $"{nameof(MultiplexedContentStore)}.");

                   foreach (var kvp in _drivesWithContentStore)
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
        public Task<StructResult<long>> RemoveFromTrackerAsync(Context context)
        {
            return RemoveFromTrackerCall<ContentStoreTracer>.RunAsync(Tracer, new OperationContext(context), async () =>
            {
                var removeTaskByStore = new Dictionary<string, Task<StructResult<long>>>();

                foreach (var kvp in _drivesWithContentStore)
                {
                    if (kvp.Value is IRepairStore store)
                    {
                        removeTaskByStore.Add(kvp.Key, store.RemoveFromTrackerAsync(context));
                    }
                }

                await Task.WhenAll(removeTaskByStore.Values);

                var sb = new StringBuilder();
                long filesTrimmed = 0;
                foreach (var kvp in removeTaskByStore)
                {
                    var removeFromTrackerResult = await kvp.Value;
                    if (removeFromTrackerResult.Succeeded)
                    {
                        filesTrimmed += removeFromTrackerResult.Data;
                    }
                    else
                    {
                        sb.Concat($"{kvp.Key} repair handling failed, error=[{removeFromTrackerResult}]", "; ");
                    }
                }

                if (sb.Length > 0)
                {
                    return new StructResult<long>(sb.ToString());
                }
                else
                {
                    return new StructResult<long>(filesTrimmed);
                }
            });
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            return PerformStoreOperationAsync<IStreamStore, OpenStreamResult>(store => store.StreamContentAsync(context, contentHash));
        }

        /// <inheritdoc />
        public Task<FileExistenceResult> CheckFileExistsAsync(Context context, ContentHash contentHash)
        {
            return PerformStoreOperationAsync<IStreamStore, FileExistenceResult>(store => store.CheckFileExistsAsync(context, contentHash));
        }

        /// <inheritdoc />
        public Task<BoolResult> HandleCopyFileRequestAsync(Context context, ContentHash hash, CancellationToken token)
        {
            return PerformStoreOperationAsync<ICopyRequestHandler, BoolResult>(store => store.HandleCopyFileRequestAsync(context, hash, token));
        }

        private async Task<TResult> PerformStoreOperationAsync<TStore, TResult>(Func<TStore, Task<TResult>> executeAsync)
            where TResult : ResultBase
        {
            TResult result = null;

            foreach (var store in GetStoresInOrder<TStore>())
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

        private IEnumerable<TStore> GetStoresInOrder<TStore>()
        {
            if (_drivesWithContentStore[_preferredCacheDrive] is TStore store)
            {
                yield return store;
            }

            foreach (var kvp in _drivesWithContentStore)
            {
                if (kvp.Key == _preferredCacheDrive)
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
                foreach (var kvp in _drivesWithContentStore)
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

            foreach (var kvp in _drivesWithContentStore)
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
            foreach (var kvp in _drivesWithContentStore)
            {
                kvp.Value.PostInitializationCompleted(context, result);
            }
        }

        /// <inheritdoc />
        public Task<PutResult> HandlePushFileAsync(Context context, ContentHash hash, AbsolutePath sourcePath, CancellationToken token)
        {
            return PerformStoreOperationAsync<IPushFileHandler, PutResult>(store => store.HandlePushFileAsync(context, hash, sourcePath, token));
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
    }
}
