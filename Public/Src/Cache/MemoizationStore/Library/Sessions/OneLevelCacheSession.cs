// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Sessions;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <nodoc />
    public class OneLevelCacheSession : ICacheSessionWithLevelSelectors, IHibernateCacheSession, ITrustedContentSession
    {
        /// <summary>
        ///     Auto-pinning behavior configuration.
        /// </summary>
        protected readonly ImplicitPin ImplicitPin;

        private IContentSession? _contentReadOnlySession;

        private IMemoizationSession? _memoizationReadOnlySession;

        /// <summary>
        ///     The content session backing the session.
        /// </summary>
        protected IContentSession ContentReadOnlySession
        {
            get
            {
                if (_disposed)
                {
                    throw new InvalidOperationException("Can't obtain an inner session because the instance was already being disposed.");
                }

                return _contentReadOnlySession!;
            }
        }

        /// <summary>
        ///     The memoization store backing the session.
        /// </summary>
        protected IMemoizationSession MemoizationReadOnlySession
        {
            get
            {
                if (_disposed)
                {
                    throw new InvalidOperationException("Can't obtain an inner session because the instance was already being disposed.");
                }

                return _memoizationReadOnlySession!;
            }
        }

        private bool _disposed;

        /// <nodoc />
        protected OneLevelCacheBase? Parent;

        /// <summary>
        ///     Initializes a new instance of the <see cref="OneLevelCacheSession" /> class.
        /// </summary>
        public OneLevelCacheSession(
            OneLevelCacheBase? parent,
            string name,
            ImplicitPin implicitPin,
            IMemoizationSession memoizationSession,
            IContentSession contentSession)
        {
            Contract.Requires(name != null);
            Contract.Requires(memoizationSession != null);
            Contract.Requires(contentSession != null);

            Parent = parent;
            Name = name;
            ImplicitPin = implicitPin;
            _memoizationReadOnlySession = memoizationSession;
            _contentReadOnlySession = contentSession;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public virtual async Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;

            var startupContentResult = await ContentReadOnlySession.StartupAsync(context).ConfigureAwait(false);
            if (!startupContentResult.Succeeded)
            {
                StartupCompleted = true;
                return new BoolResult(startupContentResult, "Content session startup failed");
            }

            var startupMemoizationResult = await MemoizationReadOnlySession.StartupAsync(context).ConfigureAwait(false);
            if (!startupMemoizationResult.Succeeded)
            {
                var sb = new StringBuilder();
                var shutdownContentResult = await ContentReadOnlySession.ShutdownAsync(context).ConfigureAwait(false);
                if (!shutdownContentResult.Succeeded)
                {
                    sb.Append($"Content session shutdown failed, error=[{shutdownContentResult}]");
                }

                sb.Append(sb.Length > 0 ? ", " : string.Empty);
                sb.Append($"Memoization session startup failed, error=[{startupMemoizationResult}]");
                StartupCompleted = true;
                return new BoolResult(sb.ToString());
            }

            StartupCompleted = true;
            return BoolResult.Success;
        }

        /// <inheritdoc />
        public virtual async Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            var shutdownMemoizationResult = MemoizationReadOnlySession != null
                ? await MemoizationReadOnlySession.ShutdownAsync(context).ConfigureAwait(false)
                : BoolResult.Success;
            var shutdownContentResult = ContentReadOnlySession != null
                ? await ContentReadOnlySession.ShutdownAsync(context).ConfigureAwait(false)
                : BoolResult.Success;

            BoolResult result;
            if (shutdownMemoizationResult.Succeeded && shutdownContentResult.Succeeded)
            {
                result = BoolResult.Success;
            }
            else
            {
                var sb = new StringBuilder();
                if (!shutdownMemoizationResult.Succeeded)
                {
                    sb.Append($"Memoization session shutdown failed, error=[{shutdownMemoizationResult}]");
                }

                if (!shutdownContentResult.Succeeded)
                {
                    sb.Append($"Content session shutdown failed, error=[{shutdownContentResult}]");
                }

                result = new BoolResult(sb.ToString());
            }

            ShutdownCompleted = true;
            return result;
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
                _memoizationReadOnlySession?.Dispose();
                _memoizationReadOnlySession = null;

                _contentReadOnlySession?.Dispose();
                _contentReadOnlySession = null;
            }
        }

        /// <inheritdoc />
        public IAsyncEnumerable<GetSelectorResult> GetSelectors(
            Context context,
            Fingerprint weakFingerprint,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return MemoizationSessionExtensions.GetSelectorsAsAsyncEnumerable(this, context, weakFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public async Task<Result<LevelSelectors>> GetLevelSelectorsAsync(
            Context context,
            Fingerprint weakFingerprint,
            CancellationToken cts,
            int level)
        {
            if (MemoizationReadOnlySession is IMemoizationSessionWithLevelSelectors withLevelSelectors)
            {
                var result = await withLevelSelectors.GetLevelSelectorsAsync(context, weakFingerprint, cts, level);

                return result;
            }

            throw new NotSupportedException(
                $"ReadOnlyMemoization session {MemoizationReadOnlySession.GetType().Name} does not support GetLevelSelectors functionality.");
        }

        /// <inheritdoc />
        public async Task<GetContentHashListResult> GetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            var result = await MemoizationReadOnlySession.GetContentHashListAsync(context, strongFingerprint, cts, urgencyHint);
            if (result.Succeeded && Parent is not null && result.ContentHashListWithDeterminism.ContentHashList is not null)
            {
                var contentHashList = result.ContentHashListWithDeterminism.ContentHashList.Hashes;
                foreach (var contentHash in contentHashList)
                {
                    Parent.AddOrExtendPin(context, contentHash, strongFingerprint.ToString());
                }
            }

            return result;
        }

        /// <inheritdoc />
        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            if (Parent is not null && Parent.CanElidePin(context, contentHash))
            {
                return PinResult.SuccessTask;
            }

            return ContentReadOnlySession.PinAsync(context, contentHash, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            PinOperationConfiguration configuration)
        {
            return Workflows.RunWithFallback(
                contentHashes,
                initialFunc: (contentHashes) =>
                             {
                                 return Task.FromResult(
                                     contentHashes.Select(
                                         contentHash =>
                                         {
                                             if (Parent is not null && Parent.CanElidePin(context, contentHash))
                                             {
                                                 return PinResult.Success;
                                             }

                                             return PinResult.ContentNotFound;
                                         }).AsIndexedTasks());
                             },
                fallbackFunc: (contentHashes) => { return ContentReadOnlySession.PinAsync(context, contentHashes, configuration); },
                isSuccessFunc: r => r.Succeeded);
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return ContentReadOnlySession.OpenStreamAsync(context, contentHash, cts, urgencyHint);
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
            UrgencyHint urgencyHint
        )
        {
            return ContentReadOnlySession.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return Workflows.RunWithFallback(
                contentHashes,
                initialFunc: (contentHashes) =>
                             {
                                 return Task.FromResult(
                                     contentHashes.Select(
                                         contentHash =>
                                         {
                                             if (Parent is not null && Parent.CanElidePin(context, contentHash))
                                             {
                                                 return PinResult.Success;
                                             }

                                             return PinResult.ContentNotFound;
                                         }).AsIndexedTasks());
                             },
                fallbackFunc: (contentHashes) => { return ContentReadOnlySession.PinAsync(context, contentHashes, cts, urgencyHint); },
                isSuccessFunc: r => r.Succeeded);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return ContentReadOnlySession.PlaceFileAsync(context, hashesWithPaths, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return ContentReadOnlySession is IHibernateContentSession session
                ? session.EnumeratePinnedContentHashes()
                : Enumerable.Empty<ContentHash>();
        }

        /// <inheritdoc />
        public Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            return ContentReadOnlySession is IHibernateContentSession session
                ? session.PinBulkAsync(context, contentHashes)
                : Task.FromResult(0);
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownEvictionAsync(Context context)
        {
            return ContentReadOnlySession is IHibernateContentSession session
                ? session.ShutdownEvictionAsync(context)
                : BoolResult.SuccessTask;
        }

        /// <inheritdoc />
        public IList<PublishingOperation> GetPendingPublishingOperations()
            => MemoizationReadOnlySession is IHibernateCacheSession session
                ? session.GetPendingPublishingOperations()
                : new List<PublishingOperation>();

        /// <inheritdoc />
        public Task SchedulePublishingOperationsAsync(Context context, IEnumerable<PublishingOperation> pendingOperations)
            => MemoizationReadOnlySession is IHibernateCacheSession session
                ? session.SchedulePublishingOperationsAsync(context, pendingOperations)
                : Task.FromResult(0);

        /// <summary>
        ///     Gets the writable content session.
        /// </summary>
        public IContentSession ContentSession => (IContentSession)ContentReadOnlySession;

        /// <summary>
        ///     Gets the writable memoization session.
        /// </summary>
        public IMemoizationSession MemoizationSession => (IMemoizationSession)MemoizationReadOnlySession;

        /// <inheritdoc />
        public async Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            var result = await MemoizationSession.AddOrGetContentHashListAsync(
                context,
                strongFingerprint,
                contentHashListWithDeterminism,
                cts,
                urgencyHint);

            if (result.Succeeded && Parent is not null && result.ContentHashListWithDeterminism.ContentHashList is not null)
            {
                var contentHashList = result.ContentHashListWithDeterminism.ContentHashList.Hashes;
                foreach (var contentHash in contentHashList)
                {
                    Parent.AddOrExtendPin(context, contentHash, strongFingerprint.ToString());
                }
            }

            return result;
        }

        /// <inheritdoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(
            Context context,
            IEnumerable<Task<StrongFingerprint>> strongFingerprints,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return MemoizationSession.IncorporateStrongFingerprintsAsync(context, strongFingerprints, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return ContentSession.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint
        )
        {
            return ContentSession.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context,
            HashType hashType,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return ContentSession.PutStreamAsync(context, hashType, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return ContentSession.PutStreamAsync(context, contentHash, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutTrustedFileAsync(Context context, ContentHashWithSize contentHashWithSize, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint)
        {
            if (ContentSession is ITrustedContentSession session)
            {
                return session.PutTrustedFileAsync(context, contentHashWithSize, path, realizationMode, cts, urgencyHint);
            }

            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public AbsolutePath? TryGetWorkingDirectory(AbsolutePath? pathHint)
        {
            if (ContentSession is ITrustedContentSession session)
            {
                return session.TryGetWorkingDirectory(pathHint);
            }

            throw new NotImplementedException();
        }
    }
}
