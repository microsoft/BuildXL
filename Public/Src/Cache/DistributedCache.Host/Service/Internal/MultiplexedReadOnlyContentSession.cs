// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.Host.Service.Internal
{
    public class MultiplexedReadOnlyContentSession : IReadOnlyContentSession, IHibernateContentSession
    {
        protected readonly IReadOnlyContentSession PreferredContentSession;
        protected readonly IDictionary<string, IReadOnlyContentSession> SessionsByCacheRoot;

        /// <summary>
        ///     Call tracer for this and derived classes.
        /// </summary>
        protected readonly ContentSessionTracer Tracer;

        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MultiplexedReadOnlyContentSession"/> class.
        /// </summary>
        public MultiplexedReadOnlyContentSession(
            ContentSessionTracer tracer,
            Dictionary<string, IReadOnlyContentSession> sessionsByCacheRoot,
            string name,
            string preferredCacheDrive)
        {
            Contract.Requires(name != null);
            Contract.Requires(preferredCacheDrive != null);
            Contract.Requires(sessionsByCacheRoot != null);
            Contract.Requires(sessionsByCacheRoot.Count > 0);

            Name = name;
            Tracer = tracer;
            SessionsByCacheRoot = sessionsByCacheRoot;

            if (!SessionsByCacheRoot.TryGetValue(preferredCacheDrive, out PreferredContentSession))
            {
                throw new ArgumentException(nameof(preferredCacheDrive));
            }
        }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            return StartupCall<ContentSessionTracer>.RunAsync(
                Tracer,
                context,
                async () =>
                {
                    StartupStarted = true;

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

                    StartupCompleted = true;
                    return finalResult;
                });
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            return ShutdownCall<ContentSessionTracer>.RunAsync(
                Tracer,
                context,
                async () =>
                {
                    ShutdownStarted = true;
                    var finalResult = BoolResult.Success;

                    foreach (var session in SessionsByCacheRoot.Values)
                    {
                        var result = await session.ShutdownAsync(context).ConfigureAwait(false);
                        if (!result.Succeeded)
                        {
                            finalResult = new BoolResult(finalResult, result.ErrorMessage);
                        }
                    }

                    ShutdownCompleted = true;
                    return finalResult;
                });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Dispose(true);
            GC.SuppressFinalize(this);

            _disposed = true;
        }

        /// <summary>
        ///     Dispose pattern.
        /// </summary>
        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var session in SessionsByCacheRoot.Values)
                {
                    session.Dispose();
                }
            }
        }

        public Task<PinResult> PinAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return PreferredContentSession.PinAsync(context, contentHash, cts, urgencyHint);
        }

        public Task<OpenStreamResult> OpenStreamAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return PreferredContentSession.OpenStreamAsync(context, contentHash, cts, urgencyHint);
        }

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
            return GetCache(path).PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }

        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return PreferredContentSession.PinAsync(context, contentHashes, cts, urgencyHint);
        }

        protected IReadOnlyContentSession GetCache(AbsolutePath path)
        {
            var drive = Path.GetPathRoot(path.Path);

            if (SessionsByCacheRoot.TryGetValue(drive, out var contentSession))
            {
                return contentSession;
            }

            return PreferredContentSession;
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
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return PreferredContentSession is IHibernateContentSession session
                ? session.EnumeratePinnedContentHashes()
                : Enumerable.Empty<ContentHash>();
        }

        /// <inheritdoc />
        public Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            return PreferredContentSession is IHibernateContentSession session
                ? session.PinBulkAsync(context, contentHashes)
                : BoolResult.SuccessTask;
        }
    }
}
