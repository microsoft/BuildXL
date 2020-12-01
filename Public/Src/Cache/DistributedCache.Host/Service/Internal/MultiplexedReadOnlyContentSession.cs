// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.Host.Service.Internal
{
    public class MultiplexedReadOnlyContentSession : StartupShutdownBase, IReadOnlyContentSession, IHibernateContentSession
    {
        /// <nodoc />
        public readonly IReadOnlyContentSession PreferredContentSession;

        /// <nodoc />
        public readonly IDictionary<string, IReadOnlyContentSession> SessionsByCacheRoot;
        protected readonly MultiplexedContentStore Store;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(MultiplexedContentSession));

        /// <inheritdoc />
        public string Name { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MultiplexedReadOnlyContentSession"/> class.
        /// </summary>
        public MultiplexedReadOnlyContentSession(
            Dictionary<string, IReadOnlyContentSession> sessionsByCacheRoot,
            string name,
            MultiplexedContentStore store)
        {
            Contract.Requires(name != null);
            Contract.Requires(sessionsByCacheRoot != null);
            Contract.Requires(sessionsByCacheRoot.Count > 0);

            Name = name;
            SessionsByCacheRoot = sessionsByCacheRoot;
            Store = store;

            if (!SessionsByCacheRoot.TryGetValue(store.PreferredCacheDrive, out PreferredContentSession))
            {
                throw new ArgumentException(nameof(store.PreferredCacheDrive));
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var finalResult = BoolResult.Success;

            var sessions = SessionsByCacheRoot.Values.ToArray();
            for (var i = 0; i < sessions.Length; i++)
            {
                var canHibernate = sessions[i] is IHibernateContentSession ? "can" : "cannot";
                Tracer.Debug(context, $"Session {sessions[i].Name} {canHibernate} hibernate");
                var startupResult = await sessions[i].StartupAsync(context).ConfigureAwait(false);

                if (!startupResult.Succeeded)
                {
                    finalResult = startupResult;
                    for (var j = 0; j < i; j++)
                    {
                        var shutdownResult = await sessions[j].ShutdownAsync(context).ConfigureAwait(false);
                        if (!shutdownResult.Succeeded)
                        {
                            finalResult = new BoolResult(finalResult, shutdownResult.ErrorMessage);
                        }
                    }
                }
            }

            return finalResult;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var finalResult = BoolResult.Success;

            foreach (var session in SessionsByCacheRoot.Values)
            {
                var result = await session.ShutdownAsync(context).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    finalResult = new BoolResult(finalResult, result.ErrorMessage);
                }
            }

            return finalResult;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            foreach (var session in SessionsByCacheRoot.Values)
            {
                session.Dispose();
            }
        }

        /// <inheritdoc />
        public Task<PinResult> PinAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return PerformAggregateSessionOperationAsync<IReadOnlyContentSession, PinResult>(
                context,
                session => session.PinAsync(context, contentHash, cts, urgencyHint),
                (r1, r2) => r1.Succeeded ? r1 : r2,
                shouldBreak: r => r.Succeeded);
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return PerformAggregateSessionOperationAsync<IReadOnlyContentSession, OpenStreamResult>(
                context,
                session => session.OpenStreamAsync(context, contentHash, cts, urgencyHint),
                (r1, r2) => r1.Succeeded ? r1 : r2,
                shouldBreak: r => r.Succeeded);
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            IContentSession hardlinkSession = null;
            if (realizationMode == FileRealizationMode.HardLink)
            {
                var drive = path.GetPathRoot();
                if (SessionsByCacheRoot.TryGetValue(drive, out var session) && session is IContentSession writeableSession)
                {
                    hardlinkSession = writeableSession;
                }
                else
                {
                    return Task.FromResult(new PlaceFileResult("Requested hardlink but there is no session on the same drive as destination path."));
                }
            }

            return PerformAggregateSessionOperationAsync<IReadOnlyContentSession, PlaceFileResult>(
                context,
                executeAsync: placeFileCore,
                (r1, r2) => r1.Succeeded ? r1 : r2,
                shouldBreak: r => r.Succeeded,
                pathHint: path);

            async Task<PlaceFileResult> placeFileCore(IReadOnlyContentSession session)
            {
                // If we exclusively want a hardlink, we should make sure that we can copy from other drives to satisfy the request.
                if (realizationMode != FileRealizationMode.HardLink || session == hardlinkSession)
                {
                    return await session.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint);
                }

                // See if session has the content.
                var streamResult = await session.OpenStreamAsync(context, contentHash, cts, urgencyHint).ThrowIfFailure();

                // Put it into correct store
                var putResult = await hardlinkSession.PutStreamAsync(context, contentHash, streamResult.Stream, cts, urgencyHint).ThrowIfFailure();

                // Try the hardlink on the correct drive.
                return await hardlinkSession.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint);
            }
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return MultiLevelUtilities.RunManyLevelAsync(
                GetSessionsInOrder<IReadOnlyContentSession>().ToArray(),
                contentHashes,
                (session, hashes) => session.PinAsync(context, hashes, cts, urgencyHint),
                p => p.Succeeded);
        }

        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context, 
            IReadOnlyList<ContentHash> contentHashes, 
            PinOperationConfiguration config)
        {
            return MultiLevelUtilities.RunManyLevelAsync(
                GetSessionsInOrder<IReadOnlyContentSession>().ToArray(),
                contentHashes,
                (session, hashes) => session.PinAsync(context, hashes, config),
                p => p.Succeeded);
        }

        protected TCache GetCache<TCache>(AbsolutePath path = null)
        {
            if (path != null)
            {
                var drive = path.GetPathRoot();
                if (SessionsByCacheRoot.TryGetValue(drive, out var contentSession))
                {
                    return (TCache)contentSession;
                }
            }

            return (TCache)PreferredContentSession;
        }

        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return MultiLevelUtilities.RunManyLevelAsync(
                GetSessionsInOrder<IReadOnlyContentSession>().ToArray(),
                hashesWithPaths,
                (session, hashes) => session.PlaceFileAsync(context, hashes, accessMode, replacementMode, realizationMode, cts, urgencyHint),
                p => p.Succeeded);
        }

        /// <inheritdoc />
        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return PerformAggregateSessionOperationCoreAsync<IHibernateContentSession, Result<IEnumerable<ContentHash>>>(
                session =>
                {
                    var hashes = session.EnumeratePinnedContentHashes();
                    return Task.FromResult(Result.Success(hashes));
                },
                (r1, r2) => Result.Success(r1.Value.Concat(r2.Value)),
                shouldBreak: r => false).GetAwaiter().GetResult().Value;
        }

        /// <inheritdoc />
        public Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            return PerformAggregateSessionOperationAsync<IHibernateContentSession, BoolResult>(
                context,
                async session =>
                {
                    await session.PinBulkAsync(context, contentHashes);
                    return BoolResult.Success;
                },
                (r1, r2) => r1 & r2,
                shouldBreak: r => false);
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownEvictionAsync(Context context)
        {
            return PerformAggregateSessionOperationAsync<IHibernateContentSession, BoolResult>(
                context,
                session => session.ShutdownEvictionAsync(context),
                (r1, r2) => r1 & r2,
                shouldBreak: r => false);
        }

        private IEnumerable<TSession> GetSessionsInOrder<TSession>(AbsolutePath path = null)
        {
            var drive = path != null ? path.GetPathRoot() : Store.PreferredCacheDrive;

            if (!SessionsByCacheRoot.ContainsKey(drive))
            {
                drive = Store.PreferredCacheDrive;
            }

            if (SessionsByCacheRoot[drive] is TSession session)
            {
                yield return session;
            }

            if (!Store.TryAllSesssions)
            {
                yield break;
            }

            foreach (var kvp in SessionsByCacheRoot)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(kvp.Key, drive))
                {
                    // Already yielded the preferred cache
                    continue;
                }

                if (kvp.Value is TSession otherSession)
                {
                    yield return otherSession;
                }
            }
        }

        private async Task<TResult> PerformSessionOperationAsync<TSession, TResult>(Func<TSession, Task<TResult>> executeAsync)
            where TResult : ResultBase
        {
            TResult result = null;

            foreach (var session in GetSessionsInOrder<TSession>())
            {
                result = await executeAsync(session);

                if (result.Succeeded)
                {
                    return result;
                }
            }

            return result ?? new ErrorResult($"Could not find a content session which implements {typeof(TSession).Name} in {nameof(MultiplexedContentSession)}.").AsResult<TResult>();
        }

        private Task<TResult> PerformAggregateSessionOperationAsync<TSession, TResult>(
            Context context,
            Func<TSession, Task<TResult>> executeAsync,
            Func<TResult, TResult, TResult> aggregate,
            Func<TResult, bool> shouldBreak,
            AbsolutePath pathHint = null,
            [CallerMemberName] string caller = null)
            where TResult : ResultBase
        {
            var operationContext = context is null ? new OperationContext() : new OperationContext(context);
            return operationContext.PerformOperationAsync(
                Tracer,
                () => PerformAggregateSessionOperationCoreAsync(executeAsync, aggregate, shouldBreak, pathHint),
                traceOperationStarted: false,
                traceOperationFinished: false,
                caller: caller);
        }

        private async Task<TResult> PerformAggregateSessionOperationCoreAsync<TSession, TResult>(
            Func<TSession, Task<TResult>> executeAsync,
            Func<TResult, TResult, TResult> aggregate,
            Func<TResult, bool> shouldBreak,
            AbsolutePath pathHint = null)
            where TResult : ResultBase
        {
            TResult result = null;

            // Go through all the sessions
            foreach (var session in GetSessionsInOrder<TSession>(pathHint))
            {
                var priorResult = result;

                try
                {
                    result = await executeAsync(session);
                }
                catch (Exception e)
                {
                    result = new ErrorResult(e).AsResult<TResult>();
                }

                // Aggregate with previous result
                if (priorResult != null)
                {
                    result = aggregate(priorResult, result);
                }

                // If result is sufficient, stop trying other stores and return result
                if (shouldBreak(result))
                {
                    return result;
                }
            }

            Contract.Check(result != null)?.Assert($"Could not find a content session which implements {typeof(TSession).Name} in {nameof(MultiplexedContentSession)}.");
            return result;
        }
    }
}
