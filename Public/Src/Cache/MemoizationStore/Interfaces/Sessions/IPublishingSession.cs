// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Utilities.Tasks;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <nodoc />
    public interface IPublishingSession : IStartupShutdownSlim
    {
        /// <nodoc />
        Task<BoolResult> PublishContentHashListAsync(
            OperationContext context,
            StrongFingerprint fingerprint,
            ContentHashListWithDeterminism contentHashList);

        /// <nodoc />
        Task<BoolResult> IncorporateStrongFingerprintsAsync(
            OperationContext context,
            IEnumerable<Task<StrongFingerprint>> strongFingerprints);
    }

    /// <nodoc />
    public interface ICachePublisher : IStartupShutdownSlim
    {
        /// <summary>
        /// Gets the unique GUID for the given cache.
        /// </summary>
        Guid CacheGuid { get; }

        /// <summary>
        /// Add a content hash list.
        /// </summary>
        Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <summary>
        /// Publish content which will be part of a content hash list.
        /// </summary>
        Task<PutResult> PutStreamAsync(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <summary>
        /// Check whether the content is already available.
        /// </summary>
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal);

        /// <nodoc />
        Task<BoolResult> IncorporateStrongFingerprintsAsync(
            OperationContext context,
            IEnumerable<Task<StrongFingerprint>> strongFingerprints);

        /// <nodoc />
        Task<GetContentHashListResult> GetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal);
    }

    /// <nodoc />
    public class CacheSessionPublisherWrapper : StartupShutdownSlimBase, ICachePublisher
    {
        /// <nodoc />
        public Guid CacheGuid => _cache.Id;

        /// <nodoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(CacheSessionPublisherWrapper));

        private readonly ICache _cache;
        private readonly ICacheSession _session;

        /// <nodoc />
        public CacheSessionPublisherWrapper(ICache cache, ICacheSession session)
        {
            Contract.RequiresNotNull(cache);
            Contract.RequiresNotNull(session);
            _cache = cache;
            _session = session;
        }

        /// <nodoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return _session.StartupAsync(context);
        }

        /// <nodoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = BoolResult.Success;
            result &= await _session.ShutdownAsync(context);
            result &= await _cache.ShutdownAsync(context);
            return result;
        }

        /// <nodoc />
        public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _session.AddOrGetContentHashListAsync(context, strongFingerprint, contentHashListWithDeterminism, cts, urgencyHint);
        }

        /// <nodoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _session.PinAsync(context, contentHashes, cts, urgencyHint);
        }

        /// <nodoc />
        public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _session.PutStreamAsync(context, contentHash, stream, cts, urgencyHint);
        }

        /// <nodoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(OperationContext context, IEnumerable<Task<StrongFingerprint>> strongFingerprints)
        {
            return _session.IncorporateStrongFingerprintsAsync(context, strongFingerprints, context.Token);
        }

        /// <nodoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return _session.GetContentHashListAsync(context, strongFingerprint, cts, urgencyHint);
        }
    }

    /// <nodoc />
    public abstract class PublishingSessionBase<TConfiguration> : StartupShutdownSlimBase, IPublishingSession
    {
        /// <nodoc />
        protected override abstract Tracer Tracer { get; }

        private readonly TConfiguration _cachePublisherConfiguration;

        /// <summary>
        /// The publishing session needs somewhere to get content from in case it needs to publish a
        /// content hash list's contents. This should point towards some locally available cache.
        /// </summary>
        private readonly IContentSession _contentSession;

        private readonly SemaphoreSlim _fingerprintPublishingGate;
        private readonly SemaphoreSlim _contentPublishingGate;
        private ICachePublisher? _cachePublisher;

        /// <nodoc />
        protected PublishingSessionBase(
            TConfiguration configuration,
            Func<IContentSession> contentSessionFactory,
            SemaphoreSlim fingerprintPublishingGate,
            SemaphoreSlim contentPublishingGate)
        {
            _cachePublisherConfiguration = configuration;
            _contentSession = contentSessionFactory();
            _fingerprintPublishingGate = fingerprintPublishingGate;
            _contentPublishingGate = contentPublishingGate;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _contentSession.StartupAsync(context).ThrowIfFailureAsync();

            _cachePublisher = await CreateCachePublisherAsync(context, _cachePublisherConfiguration).ThrowIfFailureAsync();
            await _cachePublisher!.StartupAsync(context).ThrowIfFailureAsync();

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = BoolResult.Success;
            if (_cachePublisher != null)
            {
                result &= await _cachePublisher.ShutdownAsync(context).ThrowIfFailure();
            }

            result &= await _contentSession.ShutdownAsync(context);

            return result;
        }

        /// <nodoc />
        protected Task<Result<ICachePublisher>> CreateCachePublisherAsync(
            OperationContext context,
            TConfiguration configuration)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                return Result.Success(await CreateCachePublisherCoreAsync(context, configuration));
            },
            traceOperationStarted: false);
        }

        /// <nodoc />
        protected abstract Task<ICachePublisher> CreateCachePublisherCoreAsync(
            OperationContext context,
            TConfiguration configuration);

        /// <inheritdoc />
        public Task<BoolResult> PublishContentHashListAsync(
            OperationContext context,
            StrongFingerprint fingerprint,
            ContentHashListWithDeterminism contentHashList)
        {
            Contract.Check(_cachePublisher != null)?.Assert("Startup should be run before attempting to publish.");

            Tracer.Debug(context, $"Enqueueing publish request for StrongFingerprint=[{fingerprint}], CHL=[{contentHashList.ToTraceString()}]");

            return _fingerprintPublishingGate.GatedOperationAsync(
                (timeSpentWaiting, gateCount) =>
                {
                    ContentHashList? hashListInRemote = null;
                    bool publishSkipped = false;
                    return context.PerformOperationAsync(
                        Tracer,
                        async () =>
                        {
                            var remoteResult = await _cachePublisher.GetContentHashListAsync(context, fingerprint, context.Token);
                            var localContentHashList = contentHashList.ContentHashList;
                            var remoteContentHashList = remoteResult.ContentHashListWithDeterminism.ContentHashList;
                            var isRemoteBacked = remoteResult.Succeeded
                                    && (remoteResult.ContentHashListWithDeterminism.Determinism.IsDeterministicTool
                                        || remoteResult.ContentHashListWithDeterminism.Determinism.EffectiveGuid.Equals(_cachePublisher.CacheGuid));

                            // Skip publishing when local CHL matches remote CHL & the remote is backed.
                            if (localContentHashList.Equals(remoteContentHashList) && isRemoteBacked)
                            {
                                publishSkipped = true;
                                return BoolResult.Success;
                            }

                            // Make sure to push the blob in the selector if it exists.
                            var hashesToPush = new List<ContentHash>(localContentHashList.Hashes);
                            if (!fingerprint.Selector.ContentHash.IsZero())
                            {
                                hashesToPush.Add(fingerprint.Selector.ContentHash);
                            }

                            var remotePinResults = await TaskUtilities.SafeWhenAll(await _cachePublisher.PinAsync(context, hashesToPush, context.Token));
                            var missingFromRemote = remotePinResults
                                .Where(r => !r.Item.Succeeded)
                                .Select(r => hashesToPush[r.Index])
                                .ToArray();

                            if (missingFromRemote.Length > 0)
                            {
                                await PushToRemoteAsync(context, missingFromRemote).ThrowIfFailure();
                            }

                            var addOrGetResult = await _cachePublisher.AddOrGetContentHashListAsync(context, fingerprint, contentHashList, context.Token).ThrowIfFailure();
                            hashListInRemote = addOrGetResult.ContentHashListWithDeterminism.ContentHashList;

                            return BoolResult.Success;
                        },
                        traceOperationStarted: false,
                        extraEndMessage: result =>
                            $"Skipped=[{publishSkipped}], " +
                            $"Added=[{result.Succeeded && hashListInRemote is null && !publishSkipped}], " +
                            $"StrongFingerprint=[{fingerprint}], " +
                            $"ContentHashList=[{contentHashList.ToTraceString()}], " +
                            $"TimeSpentWaiting=[{timeSpentWaiting}], " +
                            $"GateCount=[{gateCount}]");
                },
                context.Token);
        }

        /// <nodoc />
        private async Task<BoolResult> PushToRemoteAsync(OperationContext context, IReadOnlyList<ContentHash> hashes)
        {
            Contract.Check(_cachePublisher != null)?.Assert("Startup should be run before attempting to publish.");

            var localPinResult = await TaskUtilities.SafeWhenAll(await _contentSession.PinAsync(context, hashes, context.Token));
            var missingFromLocal = localPinResult.Where(r => !r.Item.Succeeded);
            if (missingFromLocal.Any())
            {
                return new BoolResult($"Can't publish fingerprint because the local cache is missing pieces of content referenced in it. Missing content hashes: {string.Join(", ", missingFromLocal.Select(m => hashes[m.Index].ToShortString()))}");
            }

            var tasks = hashes.Select(hash =>
            {
                return _contentPublishingGate.GatedOperationAsync(
                    async (timeSpentWaiting, gateCount) =>
                    {
                        var openStreamResult = await _contentSession.OpenStreamAsync(context, hash, context.Token).ThrowIfFailure();
                        var stream = openStreamResult.Stream!;
                        var putStreamResult = await _cachePublisher.PutStreamAsync(context, hash, stream, context.Token).ThrowIfFailure();
                        return Unit.Void;
                    }, context.Token);
            });

            await TaskUtilities.SafeWhenAll(tasks);

            return BoolResult.Success;
        }

        /// <nodoc />
        public Task<BoolResult> IncorporateStrongFingerprintsAsync(OperationContext context, IEnumerable<Task<StrongFingerprint>> strongFingerprints)
        {
            return _cachePublisher?.IncorporateStrongFingerprintsAsync(context, strongFingerprints) ?? Task.FromResult(new BoolResult(errorMessage: $"Attempt to call {nameof(IncorporateStrongFingerprintsAsync)} without having instantiated a {nameof(_cachePublisher)}"));
        }
    }
}
