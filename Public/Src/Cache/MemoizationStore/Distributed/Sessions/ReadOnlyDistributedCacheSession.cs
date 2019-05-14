// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Distributed.Sessions
{
    /// <summary>
    /// Read only distributed cache session
    /// </summary>
    public class ReadOnlyDistributedCacheSession : StartupShutdownBase, IReadOnlyCacheSessionWithLevelSelectors
    {
        private readonly IReadOnlyCacheSession _innerCacheSession;
        private readonly Guid _innerCacheId;

        /// <summary>
        /// Instance of <see cref="IMetadataCache"/>
        /// </summary>
        protected readonly IMetadataCache MetadataCache;

        /// <summary>
        /// Gets an instance of <see cref="MemoizationStoreTracer"/>
        /// </summary>
        protected DistributedCacheSessionTracer DistributedTracer { get; }

        /// <inheritdoc />
        protected override Tracer Tracer => DistributedTracer;

        /// <summary>
        /// Instance of logger
        /// </summary>
        protected readonly ILogger Logger;

        /// <summary>
        /// Whether or not to re-query the inner cache if the distributed value is unbacked.
        /// </summary>
        private readonly ReadThroughMode _readThroughMode;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReadOnlyDistributedCacheSession" /> class.
        /// </summary>
        public ReadOnlyDistributedCacheSession(
            ILogger logger,
            string name,
            IReadOnlyCacheSession innerCacheSession,
            Guid innerCacheId,
            IMetadataCache metadataCache,
            DistributedCacheSessionTracer tracer,
            ReadThroughMode readThroughModeMode)
        {
            Contract.Requires(logger != null);
            Contract.Requires(innerCacheSession != null);
            Contract.Requires(metadataCache != null);

            Logger = logger;
            Name = name;
            MetadataCache = metadataCache;
            _innerCacheSession = innerCacheSession;
            _innerCacheId = innerCacheId;
            DistributedTracer = tracer;
            _readThroughMode = readThroughModeMode;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context) => _innerCacheSession.StartupAsync(context);

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context) => _innerCacheSession.ShutdownAsync(context);

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            base.DisposeCore();

            _innerCacheSession?.Dispose();
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return this.GetSelectorsAsAsyncEnumerable(context, weakFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public async Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            if (_innerCacheSession is IReadOnlyMemoizationSessionWithLevelSelectors withLevelSelectors)
            {
                var result = await MetadataCache.GetOrAddSelectorsAsync(
                    context,
                    weakFingerprint,
                    fingerprint =>
                        withLevelSelectors.GetAllSelectorsAsync(context, weakFingerprint, cts));

                return LevelSelectors.Single(result);
            }

            throw new NotSupportedException($"Inner store {_innerCacheSession.GetType().Name} does not support GetLevelSelectors functionality.");
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return GetContentHashListCall.RunAsync(DistributedTracer, context, strongFingerprint, async () =>
            {
                GetContentHashListResult innerResult = null;

                // Get the value from the metadata cache or load the current inner value into it (and then return it)
                var existing = await MetadataCache.GetOrAddContentHashListAsync(context, strongFingerprint, async fingerprint =>
                {
                    innerResult = await _innerCacheSession.GetContentHashListAsync(context, fingerprint, cts, urgencyHint);
                    return innerResult;
                });

                // Check to see if we need to need to read through to the inner value.
                if (_readThroughMode == ReadThroughMode.ReadThrough &&
                    existing.Succeeded &&
                    !(existing.ContentHashListWithDeterminism.Determinism.IsDeterministic &&
                      existing.ContentHashListWithDeterminism.Determinism.Guid == _innerCacheId))
                {
                    // Read through to the inner cache because the metadata cache's value is not guaranteed to be backed.
                    if (innerResult == null)
                    {
                        // We did not already query the inner cache as part of the original query, so do that now.
                        innerResult = await _innerCacheSession.GetContentHashListAsync(context, strongFingerprint, cts, urgencyHint);
                    }

                    if (innerResult != null && innerResult.Succeeded &&
                        innerResult.ContentHashListWithDeterminism.Determinism.IsDeterministic)
                    {
                        // If the inner cache's value is now backed, clear the value from the metadata cache so that the
                        // next read will load the backed value into the metadata cache (preventing the need for future read-throughs).
                        await MetadataCache.DeleteFingerprintAsync(context, strongFingerprint).IgnoreFailure();
                    }

                    return innerResult;
                }
                else
                {
                    // Return the existing value in the metadata cache, or any error.
                    return existing;
                }
            });
        }

        /// <inheritdoc />
        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return _innerCacheSession.PinAsync(context, contentHash, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return _innerCacheSession.OpenStreamAsync(context, contentHash, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return _innerCacheSession.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _innerCacheSession.PinAsync(context, contentHashes, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _innerCacheSession.PlaceFileAsync(context, hashesWithPaths, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }
    }
}
