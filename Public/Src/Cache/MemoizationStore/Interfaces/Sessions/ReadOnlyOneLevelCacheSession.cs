// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
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
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     An IReadOnlyCacheSession implemented with one level of content and memoization.
    /// </summary>
    public class ReadOnlyOneLevelCacheSession : IReadOnlyCacheSessionWithLevelSelectors
    {
        /// <summary>
        ///     Auto-pinning behavior configuration.
        /// </summary>
        protected readonly ImplicitPin ImplicitPin;

        /// <summary>
        ///     The content store backing the session.
        /// </summary>
        protected readonly IContentStore ContentStore;

        /// <summary>
        ///     The memoization store backing the session.
        /// </summary>
        protected readonly IMemoizationStore MemoizationStore;

        /// <summary>
        ///     The content session backing the session.
        /// </summary>
        protected IReadOnlyContentSession _contentReadOnlySession;

        /// <summary>
        ///     The memoization store backing the session.
        /// </summary>
        protected IReadOnlyMemoizationSession _memoizationReadOnlySession;

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

            var startupContentResult = await _contentReadOnlySession.StartupAsync(context).ConfigureAwait(false);
            if (!startupContentResult.Succeeded)
            {
                StartupCompleted = true;
                return new BoolResult(startupContentResult, "Content session startup failed");
            }

            var startupMemoizationResult = await _memoizationReadOnlySession.StartupAsync(context).ConfigureAwait(false);
            if (!startupMemoizationResult.Succeeded)
            {
                var sb = new StringBuilder();
                var shutdownContentResult = await _contentReadOnlySession.ShutdownAsync(context).ConfigureAwait(false);
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
            var shutdownMemoizationResult = _memoizationReadOnlySession != null
                ? await _memoizationReadOnlySession.ShutdownAsync(context).ConfigureAwait(false)
                : BoolResult.Success;
            var shutdownContentResult = _contentReadOnlySession != null
                ? await _contentReadOnlySession.ShutdownAsync(context).ConfigureAwait(false)
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
        public Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return this.GetSelectorsAsAsyncEnumerable(context, weakFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            if (_memoizationReadOnlySession is IReadOnlyMemoizationSessionWithLevelSelectors withLevelSelectors)
            {
                return withLevelSelectors.GetLevelSelectorsAsync(context, weakFingerprint, cts, level);
            }

            throw new NotSupportedException($"ReadOnlyMemoization session {_memoizationReadOnlySession.GetType().Name} does not support GetLevelSelectors functionality.");
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(
            Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return _memoizationReadOnlySession.GetContentHashListAsync(context, strongFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return _contentReadOnlySession.PinAsync(context, contentHash, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(
            Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return _contentReadOnlySession.OpenStreamAsync(context, contentHash, cts, urgencyHint);
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
            return _contentReadOnlySession.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentReadOnlySession.PinAsync(context, contentHashes, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _contentReadOnlySession.PlaceFileAsync(context, hashesWithPaths, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }
    }
}
