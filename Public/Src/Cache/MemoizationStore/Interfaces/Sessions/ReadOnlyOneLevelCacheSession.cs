// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     An IReadOnlyCacheSession implemented with one level of content and memoization.
    /// </summary>
    public class ReadOnlyOneLevelCacheSession : IReadOnlyCacheSessionWithLevelSelectors, IHibernateCacheSession, IConfigurablePin
    {
        /// <summary>
        ///     Auto-pinning behavior configuration.
        /// </summary>
        protected readonly ImplicitPin ImplicitPin;

        /// <summary>
        ///     The content session backing the session.
        /// </summary>
        protected IReadOnlyContentSession ContentReadOnlySession;

        /// <summary>
        ///     The memoization store backing the session.
        /// </summary>
        protected IReadOnlyMemoizationSession MemoizationReadOnlySession;

        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReadOnlyOneLevelCacheSession" /> class.
        /// </summary>
        public ReadOnlyOneLevelCacheSession(
            string name, ImplicitPin implicitPin, IReadOnlyMemoizationSession memoizationSession, IReadOnlyContentSession contentSession)
        {
            Contract.Requires(name != null);
            Contract.Requires(memoizationSession != null);
            Contract.Requires(contentSession != null);

            Name = name;
            ImplicitPin = implicitPin;
            MemoizationReadOnlySession = memoizationSession;
            ContentReadOnlySession = contentSession;
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
                MemoizationReadOnlySession?.Dispose();
                MemoizationReadOnlySession = null;

                ContentReadOnlySession?.Dispose();
                ContentReadOnlySession = null;
            }
        }


        /// <inheritdoc />
        public System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return this.GetSelectorsAsAsyncEnumerable(context, weakFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            if (MemoizationReadOnlySession is IReadOnlyMemoizationSessionWithLevelSelectors withLevelSelectors)
            {
                return withLevelSelectors.GetLevelSelectorsAsync(context, weakFingerprint, cts, level);
            }

            throw new NotSupportedException($"ReadOnlyMemoization session {MemoizationReadOnlySession.GetType().Name} does not support GetLevelSelectors functionality.");
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(
            Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return MemoizationReadOnlySession.GetContentHashListAsync(context, strongFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return ContentReadOnlySession.PinAsync(context, contentHash, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration configuration)
        {
            return ContentReadOnlySession.PinAsync(context, contentHashes, configuration);
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(
            Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return ContentReadOnlySession.OpenStreamAsync(context, contentHash, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync
            (
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
            return ContentReadOnlySession.PinAsync(context, contentHashes, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
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
    }
}
