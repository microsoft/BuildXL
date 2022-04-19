// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Tasks;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Distributed.Sessions
{
    internal class PublishingCacheSession : StartupShutdownBase, ICacheSession, IReadOnlyCacheSessionWithLevelSelectors, IHibernateCacheSession, IConfigurablePin, IAsyncShutdown
    {
        public string Name { get; }
        protected override Tracer Tracer { get; } = new Tracer(nameof(PublishingCacheSession));

        private readonly ICacheSession _local;
        private readonly IPublishingSession _remote;

        private readonly bool _publishAsynchronously;
        private bool _hasBeenMarkedForShutdown;
        private readonly TaskCompletionSource<bool> _readyForShutdown = new TaskCompletionSource<bool>();

        private readonly ConcurrentDictionary<PublishingOperation, long> _pendingPublishingOperations = new ConcurrentDictionary<PublishingOperation, long>();
        private long _orderNumber = long.MinValue;

        public PublishingCacheSession(string name, ICacheSession local, IPublishingSession remote, bool publishAsynchronously)
        {
            Name = name;
            _local = local;
            _remote = remote;
            _publishAsynchronously = publishAsynchronously;
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            // Make sure to start up local first
            await _local.StartupAsync(context).ThrowIfFailure();
            await _remote.StartupAsync(context).ThrowIfFailure();
            return BoolResult.Success;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            // Make sure to shutdown remote first.
            var remoteResult = await _remote.ShutdownAsync(context);
            var localResult = await _local.ShutdownAsync(context);
            return remoteResult & localResult;
        }

        public async Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var operationContext = TrackShutdown(new OperationContext(context, cts));
            var result = await ((IMemoizationSession)_local).AddOrGetContentHashListAsync(
                    operationContext,
                    strongFingerprint,
                    contentHashListWithDeterminism,
                    operationContext.Context.Token,
                    urgencyHint);

            if (!result.Succeeded)
            {
                return result;
            }

            var chlToPublish = result.ContentHashListWithDeterminism.ContentHashList is null
                ? contentHashListWithDeterminism
                : result.ContentHashListWithDeterminism;

            var cancellationForPublish = _publishAsynchronously
                ? ShutdownStartedCancellationToken
                : operationContext.Context.Token;

            var publishTask = PublishContentHashListAsync(
                context,
                strongFingerprint,
                chlToPublish,
                cancellationForPublish);

            if (_publishAsynchronously)
            {
                publishTask.FireAndForget(context, traceErrorResult: true, operation: nameof(PublishContentHashListAsync));
            }
            else
            {
                var publishingResult = await publishTask;

                if (!publishingResult.Succeeded)
                {
                    return new AddOrGetContentHashListResult(publishingResult);
                }
            }

            return result;
        }

        private async Task<BoolResult> PublishContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashList,
            CancellationToken token)
        {
            Contract.Assert(!_hasBeenMarkedForShutdown, "Should not queue publish operations after the session has been marked for shutdown");

            var publishingOperation = new PublishingOperation
            {
                StrongFingerprint = strongFingerprint,
                ContentHashListWithDeterminism = contentHashList
            };

            var addResult = _pendingPublishingOperations.TryAdd(publishingOperation, Interlocked.Increment(ref _orderNumber));
            if (!addResult)
            {
                // Since the operation is already pending, we return success. It's important to note that ordering matters, since
                // publishing can overwrite the remote.
                // Example of how ordering matters:
                //  These operations are queued in this order.
                //      Publish SF(A) with CHL(A)
                //      Publish SF(A) with CHL(B)
                //      Publish SF(A) with CHL(A)
                //  If we followed the queuing order and the remote was being overwritten by every call, the end result is SF(A)->CHL(A).
                //  If we eliminate duplicates, and the first operation is still pending while we queue the third one, the result is SF(A)->CHL(B).
                // For now, we are willing to take chanses and ocassionally publish CHLs out of order.
                return BoolResult.Success;
            }

            var operationContext = new OperationContext(context, token);
            var result = await _remote.PublishContentHashListAsync(
                operationContext,
                strongFingerprint,
                contentHashList);

            _pendingPublishingOperations.TryRemove(publishingOperation, out _);

            // Make sure that we unblock shutdown once all publishing operations have been completed.
            if (_hasBeenMarkedForShutdown && _pendingPublishingOperations.Count == 0)
            {
                _readyForShutdown.TrySetResult(true);
            }

            return result;
        }

        public async Task<BoolResult> IncorporateStrongFingerprintsAsync(Context context, IEnumerable<Task<StrongFingerprint>> strongFingerprints, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var localTask = _local.IncorporateStrongFingerprintsAsync(context, strongFingerprints, cts, urgencyHint);
            var remoteTask = _remote.IncorporateStrongFingerprintsAsync(new OperationContext(context, cts), strongFingerprints);
            await TaskUtilities.SafeWhenAll(remoteTask, localTask);
            return (await localTask) & (await remoteTask);
        }

        #region Read-only session with no behavior changes
        /// <inheritdoc />
        public IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _local.GetSelectors(context, weakFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _local.GetContentHashListAsync(context, strongFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _local.PinAsync(context, contentHash, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _local.OpenStreamAsync(context, contentHash, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _local.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return ((IReadOnlyContentSession)_local).PinAsync(context, contentHashes, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _local.PlaceFileAsync(context, hashesWithPaths, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }

        #region Writeable session with no behavior changes
        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _local.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _local.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _local.PutStreamAsync(context, hashType, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return ((IContentSession)_local).PutStreamAsync(context, contentHash, stream, cts, urgencyHint);
        }
        #endregion

        #region IConfigurablePin implementation

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration config)
        {
            return _local.PinAsync(context, contentHashes, config);
        }
        #endregion

        #endregion

        #region IHibernateCacheSession implementation

        /// <inheritdoc />
        public IList<PublishingOperation> GetPendingPublishingOperations()
        {
            return _pendingPublishingOperations
                .ToArray()
                .OrderBy(kvp => kvp.Value) // Attempt to keep requests in the same order as they were submitted.
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <inheritdoc />
        public Task SchedulePublishingOperationsAsync(Context context, IEnumerable<PublishingOperation> pendingOperations)
        {
            foreach (var operation in pendingOperations)
            {
                PublishContentHashListAsync(
                    context,
                    operation.StrongFingerprint,
                    operation.ContentHashListWithDeterminism,
                    ShutdownStartedCancellationToken).FireAndForget(context, traceErrorResult: true);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region IHibernateContentSession implementation - no behavior changes

        /// <inheritdoc />
        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return _local is IHibernateContentSession session
                ? session.EnumeratePinnedContentHashes()
                : Enumerable.Empty<ContentHash>();
        }

        /// <inheritdoc />
        public Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            return _local is IHibernateContentSession session
                ? session.PinBulkAsync(context, contentHashes)
                : Task.FromResult(0);
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownEvictionAsync(Context context)
        {
            return _local is IHibernateContentSession session
                ? session.ShutdownEvictionAsync(context)
                : BoolResult.SuccessTask;
        }

        #endregion

        #region IReadOnlyCacheSessionWithLevelSelectors

        /// <inheritdoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            if (_local is IReadOnlyCacheSessionWithLevelSelectors withSelectors)
            {
                return withSelectors.GetLevelSelectorsAsync(context, weakFingerprint, cts, level);
            }

            return Task.FromResult(new Result<LevelSelectors>($"{nameof(_local)} does not implement {nameof(IReadOnlyCacheSessionWithLevelSelectors)}."));
        }

        #endregion

        #region IAsyncShutdown implementation

        /// <inheritdoc />
        public async Task<BoolResult> RequestShutdownAsync(Context context)
        {
            _hasBeenMarkedForShutdown = true;
            await _readyForShutdown.Task;

            return await ShutdownAsync(context);
        }

        #endregion
    }
}
