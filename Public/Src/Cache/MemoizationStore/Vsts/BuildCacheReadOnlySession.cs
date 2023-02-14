// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Vsts.Adapters;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using BuildXL.Utilities.ParallelAlgorithms;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using BlobIdentifier = BuildXL.Cache.ContentStore.Hashing.BlobIdentifier;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    ///     IReadOnlyCacheSession for BuildCacheCache.
    /// </summary>
    public class BuildCacheReadOnlySession : StartupShutdownSlimBase, IReadOnlyCacheSessionWithLevelSelectors
    {
        /// <inheritdoc />
        protected override Tracer Tracer => CacheTracer;

        /// <nodoc />
        protected BuildCacheCacheTracer CacheTracer;

        private const int MaxSealingErrorsToPrintOnShutdown = 10;

        /// <summary>
        ///     Public name for monitoring use.
        /// </summary>
        public const string Component = "BuildCacheSession";

        /// <summary>
        ///     The only HashType recognizable by the server.
        /// </summary>
        protected const HashType RequiredHashType = HashType.Vso0;

        /// <summary>
        ///     Size for stream buffers to temp files.
        /// </summary>
        protected const int StreamBufferSize = 16384;

        /// <summary>
        ///     Policy determining whether or not content should be automatically pinned on adds or gets.
        /// </summary>
        protected readonly ImplicitPin ImplicitPin;

        /// <summary>
        ///     Backing BuildCache http client
        /// </summary>
        internal readonly IContentHashListAdapter ContentHashListAdapter;

        /// <summary>
        ///     Backing content session
        /// </summary>
        protected readonly IBackingContentSession BackingContentSession;

        /// <summary>
        ///     Optional write-through session to allow writing-behind to BlobStore
        /// </summary>
        protected readonly IContentSession WriteThroughContentSession;

        /// <summary>
        ///     The namespace of the build cache service being communicated with.
        /// </summary>
        protected readonly string CacheNamespace;

        /// <summary>
        ///     The id of the build cache service being communicated with.
        /// </summary>
        protected readonly Guid CacheId;

        private readonly bool _enableEagerFingerprintIncorporation;
        private readonly TimeSpan _inlineFingerprintIncorporationExpiry;
        private readonly TimeSpan _eagerFingerprintIncorporationInterval;
        private readonly int _eagerFingerprintIncorporationBatchSize;

        /// <inheritdoc cref="BackingContentStoreConfiguration.RequiredContentKeepUntil"/>
        internal TimeSpan? RequiredContentKeepUntil { get; init; }

        /// <summary>
        ///     Background tracker for handling the upload/sealing of unbacked metadata.
        /// </summary>
        // ReSharper disable once InconsistentNaming
        protected BackgroundTaskTracker _taskTracker;

        /// <summary>
        ///     Keep track of all the fingerprints that need to be incorporated later
        /// </summary>
        internal readonly FingerprintTracker FingerprintTracker;

        private readonly int _maxFingerprintSelectorsToFetch;
        private readonly DisposableDirectory _tempDirectory;
        private readonly bool _sealUnbackedContentHashLists;
        private readonly List<string> _sealingErrorsToPrintOnShutdown;
        private readonly bool _fingerprintIncorporationEnabled;
        private readonly int _maxDegreeOfParallelismForIncorporateRequests;
        private readonly int _maxFingerprintsPerIncorporateRequest;
        private int _sealingErrorCount;
        private readonly bool _overrideUnixFileAccessMode;

        private Context _eagerFingerprintIncorporationTracingContext; // must be set at StartupAsync
        private readonly NagleQueue<StrongFingerprint> _eagerFingerprintIncorporationNagleQueue;

        /// <nodoc />
        protected readonly bool ManuallyExtendContentLifetime;

        /// <nodoc />
        protected readonly bool ForceUpdateOnAddContentHashList;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BuildCacheReadOnlySession"/> class.
        /// </summary>
        /// <param name="fileSystem">A interface to read/write files.</param>
        /// <param name="name">Session name.</param>
        /// <param name="implicitPin">Policy determining whether or not content should be automatically pinned on adds or gets.</param>
        /// <param name="cacheNamespace">the namespace of the cache in VSTS</param>
        /// <param name="cacheId">the guid of the cache in VSTS</param>
        /// <param name="contentHashListAdapter">Backing BuildCache http client.</param>
        /// <param name="backingContentSession">Backing content session.</param>
        /// <param name="maxFingerprintSelectorsToFetch">Maximum number of selectors to enumerate.</param>
        /// <param name="minimumTimeToKeepContentHashLists">Minimum time-to-live for created or referenced ContentHashLists.</param>
        /// <param name="rangeOfTimeToKeepContentHashLists">Range of time beyond the minimum for the time-to-live of created or referenced ContentHashLists.</param>
        /// <param name="fingerprintIncorporationEnabled">Feature flag to enable fingerprints incorporation</param>
        /// <param name="maxDegreeOfParallelismForIncorporateRequests">Throttle the number of fingerprints chunks sent in parallel</param>
        /// <param name="maxFingerprintsPerIncorporateRequest">Max fingerprints allowed per chunk</param>
        /// <param name="writeThroughContentSession">Optional write-through session to allow writing-behind to BlobStore</param>
        /// <param name="sealUnbackedContentHashLists">If true, the client will attempt to seal any unbacked ContentHashLists that it sees.</param>
        /// <param name="overrideUnixFileAccessMode">If true, overrides default Unix file access modes when placing files.</param>
        /// <param name="tracer">A tracer for logging and perf counters.</param>
        /// <param name="enableEagerFingerprintIncorporation"><see cref="BuildCacheServiceConfiguration.EnableEagerFingerprintIncorporation"/></param>
        /// <param name="inlineFingerprintIncorporationExpiry"><see cref="BuildCacheServiceConfiguration.InlineFingerprintIncorporationExpiryHours"/></param>
        /// <param name="eagerFingerprintIncorporationInterval"><see cref="BuildCacheServiceConfiguration.EagerFingerprintIncorporationNagleIntervalMinutes"/></param>
        /// <param name="eagerFingerprintIncorporationBatchSize"><see cref="BuildCacheServiceConfiguration.EagerFingerprintIncorporationNagleBatchSize"/></param>
        /// <param name="manuallyExtendContentLifetime">Whether to manually extend content lifetime when doing incorporate calls</param>
        /// <param name="forceUpdateOnAddContentHashList">Whether to force an update and ignore existing CHLs when adding.</param>
        public BuildCacheReadOnlySession(
            IAbsFileSystem fileSystem,
            string name,
            ImplicitPin implicitPin,
            string cacheNamespace,
            Guid cacheId,
            IContentHashListAdapter contentHashListAdapter,
            IBackingContentSession backingContentSession,
            int maxFingerprintSelectorsToFetch,
            TimeSpan minimumTimeToKeepContentHashLists,
            TimeSpan rangeOfTimeToKeepContentHashLists,
            bool fingerprintIncorporationEnabled,
            int maxDegreeOfParallelismForIncorporateRequests,
            int maxFingerprintsPerIncorporateRequest,
            IContentSession writeThroughContentSession,
            bool sealUnbackedContentHashLists,
            bool overrideUnixFileAccessMode,
            BuildCacheCacheTracer tracer,
            bool enableEagerFingerprintIncorporation,
            TimeSpan inlineFingerprintIncorporationExpiry,
            TimeSpan eagerFingerprintIncorporationInterval,
            int eagerFingerprintIncorporationBatchSize,
            bool manuallyExtendContentLifetime,
            bool forceUpdateOnAddContentHashList)
        {
            Contract.Requires(name != null);
            Contract.Requires(contentHashListAdapter != null);
            Contract.Requires(backingContentSession != null);
            Contract.Requires(!backingContentSession.StartupStarted);

            Name = name;
            ImplicitPin = implicitPin;
            ContentHashListAdapter = contentHashListAdapter;
            BackingContentSession = backingContentSession;
            _maxFingerprintSelectorsToFetch = maxFingerprintSelectorsToFetch;
            CacheNamespace = cacheNamespace;
            CacheId = cacheId;
            WriteThroughContentSession = writeThroughContentSession;
            _sealUnbackedContentHashLists = sealUnbackedContentHashLists;
            CacheTracer = tracer;
            _enableEagerFingerprintIncorporation = enableEagerFingerprintIncorporation;
            _inlineFingerprintIncorporationExpiry = inlineFingerprintIncorporationExpiry;
            _eagerFingerprintIncorporationInterval = eagerFingerprintIncorporationInterval;
            _eagerFingerprintIncorporationBatchSize = eagerFingerprintIncorporationBatchSize;

            _tempDirectory = new DisposableDirectory(fileSystem);
            _sealingErrorsToPrintOnShutdown = new List<string>();
            _fingerprintIncorporationEnabled = fingerprintIncorporationEnabled;
            _maxDegreeOfParallelismForIncorporateRequests = maxDegreeOfParallelismForIncorporateRequests;
            _maxFingerprintsPerIncorporateRequest = maxFingerprintsPerIncorporateRequest;
            _overrideUnixFileAccessMode = overrideUnixFileAccessMode;

            ManuallyExtendContentLifetime = manuallyExtendContentLifetime;

            FingerprintTracker = new FingerprintTracker(DateTime.UtcNow + minimumTimeToKeepContentHashLists, rangeOfTimeToKeepContentHashLists);

            ForceUpdateOnAddContentHashList = forceUpdateOnAddContentHashList;

            if (enableEagerFingerprintIncorporation)
            {
                _eagerFingerprintIncorporationNagleQueue = NagleQueue<StrongFingerprint>.Create(IncorporateBatchAsync, maxDegreeOfParallelismForIncorporateRequests, eagerFingerprintIncorporationInterval, eagerFingerprintIncorporationBatchSize);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _taskTracker?.Dispose();
            BackingContentSession?.Dispose();
            WriteThroughContentSession?.Dispose();
            _tempDirectory.Dispose();
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            _eagerFingerprintIncorporationTracingContext = context;

            LogIncorporateOptions(context);

            var backingContentSessionTask = Task.Run(async () => await BackingContentSession.StartupAsync(context).ConfigureAwait(false));
            var writeThroughContentSessionResult = WriteThroughContentSession != null
                ? await WriteThroughContentSession.StartupAsync(context).ConfigureAwait(false)
                : BoolResult.Success;
            var backingContentSessionResult = await backingContentSessionTask.ConfigureAwait(false);
            if (backingContentSessionResult.Succeeded && writeThroughContentSessionResult.Succeeded)
            {
                _taskTracker = new BackgroundTaskTracker(Component, context.CreateNested(Component));
                return BoolResult.Success;
            }

            var sb = new StringBuilder();

            if (backingContentSessionResult.Succeeded)
            {
                var r = await BackingContentSession.ShutdownAsync(context).ConfigureAwait(false);
                if (!r.Succeeded)
                {
                    sb.Append($"Backing content session shutdown failed, error=[{r}]");
                }
            }
            else
            {
                sb.Append($"Backing content session startup failed, error=[{backingContentSessionResult}]");
            }

            if (writeThroughContentSessionResult.Succeeded)
            {
                var r = WriteThroughContentSession != null
                    ? await WriteThroughContentSession.ShutdownAsync(context).ConfigureAwait(false)
                    : BoolResult.Success;
                if (!r.Succeeded)
                {
                    sb.Append(sb.Length > 0 ? ", " : string.Empty);
                    sb.Append($"Write-through content session shutdown failed, error=[{r}]");
                }
            }
            else
            {
                sb.Append(sb.Length > 0 ? ", " : string.Empty);
                sb.Append($"Write-through content session startup failed, error=[{writeThroughContentSessionResult}]");
            }

            return new BoolResult(sb.ToString());
        }

        private void LogIncorporateOptions(Context context)
        {
            CacheTracer.Debug(context, $"BuildCacheReadOnlySession incorporation options: FingerprintIncorporationEnabled={_fingerprintIncorporationEnabled}, EnableEagerFingerprintIncorporation={_enableEagerFingerprintIncorporation} " +
                          $"InlineFingerprintIncorporationExpiry={_inlineFingerprintIncorporationExpiry}, " +
                          $"EagerFingerprintIncorporationInterval={_eagerFingerprintIncorporationInterval}, EagerFingerprintIncorporationBatchSize={_eagerFingerprintIncorporationBatchSize}.");
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _eagerFingerprintIncorporationNagleQueue?.Dispose();

            CacheTracer.Debug(context, "IncorporateOnShutdown start");
            CacheTracer.Debug(context, $"Incorporate fingerprints feature enabled:[{_fingerprintIncorporationEnabled}]");
            CacheTracer.Debug(context, $"Total fingerprints to be incorporated:[{FingerprintTracker.Count}]");
            CacheTracer.Debug(context, $"Max fingerprints per incorporate request(=chunk size):[{_maxFingerprintsPerIncorporateRequest}]");
            CacheTracer.Debug(context, $"Max incorporate requests allowed in parallel:[{_maxDegreeOfParallelismForIncorporateRequests}]");
            if (_fingerprintIncorporationEnabled)
            {
                // Incorporating all of the fingerprints for a build, in one request, to a single endpoint causes pain. Incorporation involves
                // extending the lifetime of all fingerprints *and* content/s mapped to each fingerprint. Processing a large request payload
                // results in, potentially, fanning out a massive number of "lifetime extend" requests to itemstore and blobstore, which can
                // bring down the endpoint. Break this down into chunks so that multiple, load-balanced endpoints can share the burden.
                List<StrongFingerprint> fingerprintsToBump = FingerprintTracker.StaleFingerprints.ToList();
                CacheTracer.Debug(context, $"Total fingerprints to be sent in incorporation requests to the service: {fingerprintsToBump.Count}");

                List<List<StrongFingerprintAndExpiration>> chunks = fingerprintsToBump.Select(
                    strongFingerprint => new StrongFingerprintAndExpiration(strongFingerprint, FingerprintTracker.GenerateNewExpiration())
                    ).GetPages(_maxFingerprintsPerIncorporateRequest).ToList();
                CacheTracer.Debug(context, $"Total fingerprint incorporation requests to be issued(=number of fingerprint chunks):[{chunks.Count}]");

                var incorporateBlock = ActionBlockSlim.Create<List<StrongFingerprintAndExpiration>>(
                    degreeOfParallelism: _maxDegreeOfParallelismForIncorporateRequests,
                    processItemAction: async chunk =>
                    {
                        var pinResult = await PinContentManuallyAsync(new OperationContext(context, CancellationToken.None), chunk);
                        if (!pinResult)
                        {
                            return;
                        }

                        await ContentHashListAdapter.IncorporateStrongFingerprints(
                            context,
                            CacheNamespace,
                            new IncorporateStrongFingerprintsRequest(chunk.AsReadOnly())
                            ).ConfigureAwait(false);
                    });

                foreach (var chunk in chunks)
                {
                    await incorporateBlock.PostAsync(chunk);
                }

                incorporateBlock.Complete();
                await incorporateBlock.Completion.ConfigureAwait(false); // TODO: Gracefully handle exceptions so that the rest of shutdown can happen (bug 1365340)
                CacheTracer.Debug(context, "IncorporateOnShutdown stop");
            }

            if (_taskTracker != null)
            {
                await _taskTracker.Synchronize().ConfigureAwait(false);
                await _taskTracker.ShutdownAsync(context).ConfigureAwait(false);
            }

            var backingContentSessionTask = Task.Run(async () => await BackingContentSession.ShutdownAsync(context).ConfigureAwait(false));
            var writeThroughContentSessionResult = WriteThroughContentSession != null
                ? await WriteThroughContentSession.ShutdownAsync(context).ConfigureAwait(false)
                : BoolResult.Success;
            var backingContentSessionResult = await backingContentSessionTask.ConfigureAwait(false);

            BoolResult result;
            if (backingContentSessionResult.Succeeded && writeThroughContentSessionResult.Succeeded)
            {
                if (_sealingErrorsToPrintOnShutdown.Any())
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Error(s) during background sealing:");
                    foreach (var sealingError in _sealingErrorsToPrintOnShutdown)
                    {
                        sb.AppendLine($"[{sealingError}]");
                    }

                    if (_sealingErrorCount > MaxSealingErrorsToPrintOnShutdown)
                    {
                        sb.AppendLine($"See log for the other {MaxSealingErrorsToPrintOnShutdown - _sealingErrorCount} error(s).");
                    }

                    result = new BoolResult(sb.ToString());
                }
                else
                {
                    result = BoolResult.Success;
                }
            }
            else
            {
                var sb = new StringBuilder();
                if (!backingContentSessionResult.Succeeded)
                {
                    sb.Append($"Backing content session shutdown failed, error=[{backingContentSessionResult}]");
                }

                if (!writeThroughContentSessionResult.Succeeded)
                {
                    sb.Append(sb.Length > 0 ? ", " : string.Empty);
                    sb.Append($"Write-through content session shutdown failed, error=[{writeThroughContentSessionResult}]");
                }

                result = new BoolResult(sb.ToString());
            }

            return result;
        }

        private async Task IncorporateBatchAsync(List<StrongFingerprint> fingerprints)
        {
            // Tracking shutdown to avoid using nagle queue after the shutdown.
            using var shutdownContext = TrackShutdown(_eagerFingerprintIncorporationTracingContext);

            var context = shutdownContext.Context;
            BoolResult result = await context.CreateOperation(
                CacheTracer,
                async () =>
                {
                    CacheTracer.Debug(context, $"IncorporateBatch: Total fingerprints to be incorporated {fingerprints.Count}, ChunkSize={_maxFingerprintsPerIncorporateRequest}, DegreeOfParallelism={_maxDegreeOfParallelismForIncorporateRequests}.");

                    // Incorporating all of the fingerprints for a build, in one request, to a single endpoint causes pain. Incorporation involves
                    // extending the lifetime of all fingerprints *and* content/s mapped to each fingerprint. Processing a large request payload
                    // results in, potentially, fanning out a massive number of "lifetime extend" requests to itemstore and blobstore, which can
                    // bring down the endpoint. Break this down into chunks so that multiple, load-balanced endpoints can share the burden.

                    var fingerprintsWithExpiration =
                        fingerprints
                            .Select(strongFingerprint => new StrongFingerprintAndExpiration(strongFingerprint, FingerprintTracker.GenerateNewExpiration()))
                            .ToList().AsReadOnly();

                    var pinResult = await PinContentManuallyAsync(context, fingerprintsWithExpiration);
                    if (!pinResult)
                    {
                        return pinResult;
                    }

                    await ContentHashListAdapter.IncorporateStrongFingerprints(
                        context,
                        CacheNamespace,
                        new IncorporateStrongFingerprintsRequest(fingerprintsWithExpiration)
                    ).ConfigureAwait(false);

                    return BoolResult.Success;
                }).RunAsync();

            // Ignoring the failure, because it was already traced if needed.
            result.IgnoreFailure();
        }

        private async Task IncorporateFingerprintAsync(OperationContext context, StrongFingerprint fingerprint)
        {
            BoolResult result = await context.CreateOperation(
                CacheTracer,
                async () =>
                {
                    var fingerprintWithExpiration = new StrongFingerprintAndExpiration(fingerprint, FingerprintTracker.GenerateNewExpiration());

                    var pinResult = await PinContentManuallyAsync(context, new StrongFingerprintAndExpiration[] { fingerprintWithExpiration });
                    if (!pinResult)
                    {
                        return pinResult;
                    }
                    
                    await ContentHashListAdapter.IncorporateStrongFingerprints(
                        context,
                        CacheNamespace,
                        new IncorporateStrongFingerprintsRequest(new []{ fingerprintWithExpiration})
                    ).ConfigureAwait(false);

                    return BoolResult.Success;
                }).RunAsync();

            // Ignoring the failure, because it was already traced if needed.
            result.IgnoreFailure();
        }

        private async Task<BoolResult> PinContentManuallyAsync(OperationContext context, IEnumerable<StrongFingerprintAndExpiration> fingerprints)
        {
            // Pin the content manually as a workaround for the fact that BuildCache doesn't know how to talk to the backing content store due to it being
            // dedup or in a non-default domain and we want the content to be there even if we have to tell BuildCache that the ContentHashList is unbacked.
            if (ManuallyExtendContentLifetime)
            {
                // TODO: optimize and run in parallel if needed.
                foreach (var fingerprint in fingerprints)
                {
                    // TODO: Get the content hash list in a more efficient manner which does not require us to talk to BuildCache.
                    var hashListResult = await ContentHashListAdapter.GetContentHashListAsync(context, CacheNamespace, fingerprint.StrongFingerprint);
                    if (!hashListResult.Succeeded || hashListResult.Value?.ContentHashListWithDeterminism.ContentHashList == null)
                    {
                        return new BoolResult(hashListResult, "Failed to get content hash list when attempting to extend its conetnts' lifetimes.");
                    }

                    var expirationDate = new DateTime(Math.Max(hashListResult.Value.GetRawExpirationTimeUtc()?.Ticks ?? 0, fingerprint.ExpirationDateUtc.Ticks), DateTimeKind.Utc);

                    var pinResults = await Task.WhenAll(await BackingContentSession.PinAsync(
                        context,
                        hashListResult.Value.ContentHashListWithDeterminism.ContentHashList.Hashes,
                        expirationDate));

                    if (pinResults.Any(r => !r.Succeeded))
                    {
                        return new BoolResult($"Failed to pin all pieces of content for fingerprint=[{fingerprint.StrongFingerprint}]");
                    }
                }
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        public System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return this.GetSelectorsAsAsyncEnumerable(context, weakFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public async Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            var result = await GetSelectorsAsync(new OperationContext(context, cts), weakFingerprint);
            return LevelSelectors.Single(result);
        }

        private async Task<Result<Selector[]>> GetSelectorsAsync(OperationContext context, Fingerprint weakFingerprint)
        {
            CacheTracer.MemoizationStoreTracer.GetSelectorsStart(context, weakFingerprint);
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                var responseResult = await ContentHashListAdapter.GetSelectorsAsync(
                    context,
                    CacheNamespace,
                    weakFingerprint,
                    _maxFingerprintSelectorsToFetch).ConfigureAwait(false);

                if (!responseResult)
                {
                    return Result.FromError<Selector[]>(responseResult);
                }

                if (responseResult.Value == null)
                {
                    return Result.Success(CollectionUtilities.EmptyArray<Selector>());
                }

                foreach (var selectorAndPossible in responseResult.Value)
                {
                    var selector = selectorAndPossible.Selector;
                    if (selectorAndPossible.ContentHashList != null)
                    {
                        // Store pre-fetched data in-memory
                        var strongFingerprint = new StrongFingerprint(weakFingerprint, selector);
                        var unpackResult = UnpackContentHashListWithDeterminismAfterGet(
                            selectorAndPossible.ContentHashList,
                            CacheId);
                        if (unpackResult && unpackResult.ContentHashListWithDeterminism.Determinism.IsDeterministic)
                        {
                            CacheTracer.RecordPrefetchedContentHashList();
                            ContentHashListWithDeterminismCache.Instance.AddValue(
                                CacheNamespace,
                                strongFingerprint,
                                unpackResult.ContentHashListWithDeterminism);

                            BackingContentSession.ExpiryCache.AddExpiry(
                                selector.ContentHash,
                                unpackResult.ContentHashListWithDeterminism.Determinism.ExpirationUtc);
                        }
                    }
                }

                CacheTracer.MemoizationStoreTracer.GetSelectorsCount(context, weakFingerprint, responseResult.Value.Count());
                return responseResult.Value.Select(responseData => responseData.Selector).ToArray();
            }
            catch (Exception e)
            {
                return Result.FromException<Selector[]>(e);
            }
            finally
            {
                CacheTracer.MemoizationStoreTracer.GetSelectorsStop(context, sw.Elapsed, weakFingerprint);
            }
        }

        /// <summary>
        ///     Stores the DownloadUris in-memory to reduce the calls to BlobStore.
        /// </summary>
        protected void StorePrefetchedDownloadUris(IDictionary<string, Uri> blobDownloadUris)
        {
            if (blobDownloadUris == null)
            {
                return;
            }

            foreach (var blobDownloadUri in blobDownloadUris)
            {
                BackingContentSession.UriCache.AddDownloadUri(
                    BlobIdentifier.Deserialize(blobDownloadUri.Key).ToContentHash(),
                    new PreauthenticatedUri(blobDownloadUri.Value, EdgeType.Unknown)); // EdgeType value shouldn't matter because we don't use it.
            }
        }

        /// <inheritdoc />
        public Task<GetContentHashListResult> GetContentHashListAsync(
            Context context,
            StrongFingerprint strongFingerprint,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            var operationContext = new OperationContext(context, cts);
            return operationContext.PerformOperationAsync(
                CacheTracer,
                async () =>
                {
                    // Check for pre-fetched data
                    ContentHashListWithDeterminism contentHashListWithDeterminism;

                    if (ContentHashListWithDeterminismCache.Instance.TryGetValue(
                        CacheNamespace, strongFingerprint, out contentHashListWithDeterminism))
                    {
                        CacheTracer.RecordUseOfPrefetchedContentHashList();
                        await TrackFingerprintAsync(
                            operationContext,
                            strongFingerprint,
                            contentHashListWithDeterminism.Determinism.ExpirationUtc,
                            contentHashListWithDeterminism.ContentHashList).ConfigureAwait(false);
                        return new GetContentHashListResult(contentHashListWithDeterminism);
                    }

                    // No pre-fetched data. Need to query the server.
                    Result<ContentHashListWithCacheMetadata> responseObject =
                        await ContentHashListAdapter.GetContentHashListAsync(operationContext, CacheNamespace, strongFingerprint).ConfigureAwait(false);

                    if (!responseObject.Succeeded)
                    {
                        return new GetContentHashListResult(responseObject);
                    }

                    ContentHashListWithCacheMetadata response = responseObject.Value;
                    if (response.ContentHashListWithDeterminism.ContentHashList == null)
                    {
                        // Miss
                        return new GetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None));
                    }

                    GetContentHashListResult unpackResult = UnpackContentHashListWithDeterminismAfterGet(response, CacheId);
                    if (!unpackResult.Succeeded)
                    {
                        return unpackResult;
                    }

                    SealIfNecessaryAfterGet(operationContext, strongFingerprint, response);

                    await TrackFingerprintAsync(operationContext, strongFingerprint, response.GetRawExpirationTimeUtc(), unpackResult.ContentHashListWithDeterminism.ContentHashList);
                    return new GetContentHashListResult(unpackResult.ContentHashListWithDeterminism);
                },
                traceOperationStarted: true,
                extraStartMessage: $"StrongFingerprint=({strongFingerprint})",
                extraEndMessage: result => $"StrongFingerprint=({strongFingerprint})");
        }

        /// <nodoc />
        protected async Task TrackFingerprintAsync(OperationContext context, StrongFingerprint strongFingerprint, DateTime? expirationUtc, ContentHashList hashes)
        {
            context.Token.ThrowIfCancellationRequested();
            if (expirationUtc != null)
            {
                BackingContentSession.ExpiryCache.AddExpiry(strongFingerprint.Selector.ContentHash, expirationUtc.Value);

                if (hashes != null)
                {
                    foreach (var hash in hashes.Hashes)
                    {
                        BackingContentSession.ExpiryCache.AddExpiry(hash, expirationUtc.Value);
                    }
                }
            }

            // Currently we have 3 ways for fingerprint incorporation:
            // 1. Inline incorporation: If eager fingerprint incorporation enabled and
            //                          the entry will expire in _inlineFingerprintIncorporationExpiry time.
            // 2. Eager bulk incorporation: if eager fingerprint incorporation enabled and
            //                          the entry's expiry is not available or it won't expire in _inlineFingerprintIncorporationExpiry time.
            // 3. Session shutdown incorporation: if eager fingerprint incorporation is disabled and the normal fingerprint incorporation is enabled.
            if (_enableEagerFingerprintIncorporation)
            {
                if (expirationUtc != null && (expirationUtc.Value - DateTime.UtcNow < _inlineFingerprintIncorporationExpiry))
                {
                    CacheTracer.Debug(context, $"Incorporating fingerprint inline: StrongFingerprint=[{strongFingerprint}], ExpirationUtc=[{expirationUtc}].");

                    await IncorporateFingerprintAsync(context, strongFingerprint);
                }
                else
                {
                    // We either don't have an expiration time or the time to expiry is greater then _inlineFingerprintIncorporationExpiry
                    Contract.Assert(_eagerFingerprintIncorporationNagleQueue != null);
                    _eagerFingerprintIncorporationNagleQueue.Enqueue(strongFingerprint);
                }
            }
            else
            {
                FingerprintTracker.Track(strongFingerprint, expirationUtc);
            }
        }

        /// <inheritdoc />
        public Task<PinResult> PinAsync(
            Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return PinCall<ContentSessionTracer>.RunAsync(CacheTracer.ContentSessionTracer, new OperationContext(context), contentHash, async () =>
            {
                var bulkResults = await PinAsync(context, new[] { contentHash }, cts, urgencyHint);
                return await bulkResults.SingleAwaitIndexed();
            });
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(
            Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return OpenStreamCall<ContentSessionTracer>.RunAsync(CacheTracer.ContentSessionTracer, new OperationContext(context), contentHash, async () =>
                {
                    if (WriteThroughContentSession != null)
                    {
                        var result =
                            await WriteThroughContentSession.OpenStreamAsync(context, contentHash, cts, urgencyHint).ConfigureAwait(false);
                        if (result.Succeeded || result.Code != OpenStreamResult.ResultCode.ContentNotFound)
                        {
                            return result;
                        }
                    }

                    return await BackingContentSession.OpenStreamAsync(context, contentHash, cts, urgencyHint).ConfigureAwait(false);
                });
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
            UrgencyHint urgencyHint)
        {
            return PlaceFileCall<ContentSessionTracer>.RunAsync(CacheTracer.ContentSessionTracer, new OperationContext(context), contentHash, path, accessMode, replacementMode, realizationMode, async () =>
                {
                    if (WriteThroughContentSession != null)
                    {
                        var writeThroughResult = await WriteThroughContentSession.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint).ConfigureAwait(false);
                        if (writeThroughResult.Succeeded || writeThroughResult.Code != PlaceFileResult.ResultCode.NotPlacedContentNotFound)
                        {
                            UnixHelpers.OverrideFileAccessMode(_overrideUnixFileAccessMode, path.Path);
                            return writeThroughResult;
                        }
                    }

                    var backingResult = await BackingContentSession.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint);
                    UnixHelpers.OverrideFileAccessMode(_overrideUnixFileAccessMode, path.Path);
                    return backingResult;
                });
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return PinHelperAsync(contentHashes, (session, hashes) => session.PinAsync(context, hashes, cts, urgencyHint));
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration config)
        {
            return PinHelperAsync(contentHashes, (session, hashes) => session.PinAsync(context, hashes, config));
        }

        private Task<IEnumerable<Task<Indexed<PinResult>>>> PinHelperAsync(
            IReadOnlyList<ContentHash> contentHashes,
            Func<IReadOnlyContentSession, IReadOnlyList<ContentHash>, Task<IEnumerable<Task<Indexed<PinResult>>>>> pinAsync)
        {
            var requiredExpiry = DateTime.UtcNow + RequiredContentKeepUntil;
            return Workflows.RunWithFallback(
                    contentHashes,
                    hashes =>
                    {
                        return Task.FromResult(hashes.Select(hash => CheckExpiryCache(hash, requiredExpiry)).ToList().AsIndexedTasks());
                    },
                    hashes =>
                    {
                        if (WriteThroughContentSession == null)
                        {
                            return pinAsync(BackingContentSession, hashes);
                        }

                        return Workflows.RunWithFallback(
                            hashes,
                            hashes => pinAsync(WriteThroughContentSession, hashes),
                            hashes => pinAsync(BackingContentSession, hashes),
                            result => result.Succeeded);
                    },
                    result => result.Succeeded);
        }

        private PinResult CheckExpiryCache(ContentHash hash, DateTime? requiredExpiry)
        {
            if (requiredExpiry != null && BackingContentSession.ExpiryCache.TryGetExpiry(hash, out var expiry) && (expiry >= requiredExpiry))
            {
                return PinResult.Success;
            }
            else
            {
                return PinResult.ContentNotFound;
            }
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected async Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            ContentAvailabilityGuarantee guarantee)
        {
            using var shutdownContext = TrackShutdown(context);

            context = shutdownContext.Context;

            try
            {
                DateTime expirationUtc = FingerprintTracker.GenerateNewExpiration();
                var valueToAdd = new ContentHashListWithCacheMetadata(
                    contentHashListWithDeterminism, expirationUtc, guarantee);

                CacheTracer.Debug(
                            context,
                    $"Adding contentHashList=[{valueToAdd.ContentHashListWithDeterminism.ContentHashList}] determinism=[{valueToAdd.ContentHashListWithDeterminism.Determinism}] to VSTS with contentAvailabilityGuarantee=[{valueToAdd.ContentGuarantee}], expirationUtc=[{expirationUtc}], forceUpdate=[{ForceUpdateOnAddContentHashList}]");

                var contentHashListResponseObject =
                    await ContentHashListAdapter.AddContentHashListAsync(
                        context,
                        CacheNamespace,
                        strongFingerprint,
                        valueToAdd,
                        forceUpdate: ForceUpdateOnAddContentHashList).ConfigureAwait(false);

                if (!contentHashListResponseObject.Succeeded)
                {
                    return new AddOrGetContentHashListResult(contentHashListResponseObject);
                }

                var contentHashListResponse = contentHashListResponseObject.Value;
                var inconsistencyErrorMessage = CheckForResponseInconsistency(contentHashListResponse);
                if (inconsistencyErrorMessage != null)
                {
                    return new AddOrGetContentHashListResult(inconsistencyErrorMessage);
                }

                ContentHashList contentHashListToReturn = UnpackContentHashListAfterAdd(
                    contentHashListWithDeterminism.ContentHashList, contentHashListResponse);

                CacheDeterminism determinismToReturn = UnpackDeterminism(contentHashListResponse, CacheId);
                if (guarantee == ContentAvailabilityGuarantee.AllContentBackedByCache && !determinismToReturn.IsDeterministic)
                {
                    return new AddOrGetContentHashListResult(
                            "Inconsistent BuildCache service response. Unbacked values should never override backed values.");
                }

                await TrackFingerprintAsync(context, strongFingerprint, contentHashListResponse.GetRawExpirationTimeUtc(), contentHashListToReturn).ConfigureAwait(false);
                return new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(contentHashListToReturn, determinismToReturn));
            }
            catch (Exception e)
            {
                return new AddOrGetContentHashListResult(e);
            }
        }

        private static GetContentHashListResult UnpackContentHashListWithDeterminismAfterGet(
            ContentHashListWithCacheMetadata cacheMetadata, Guid cacheId)
        {
            var inconsistencyErrorMessage = CheckForResponseInconsistency(cacheMetadata);
            if (inconsistencyErrorMessage != null)
            {
                return new GetContentHashListResult(inconsistencyErrorMessage);
            }

            if (cacheMetadata?.ContentHashListWithDeterminism.ContentHashList == null)
            {
                // Miss
                return new GetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None));
            }

            ContentHashList contentHashList = cacheMetadata.ContentHashListWithDeterminism.ContentHashList;
            CacheDeterminism determinism = UnpackDeterminism(cacheMetadata, cacheId);

            return new GetContentHashListResult(new ContentHashListWithDeterminism(contentHashList, determinism));
        }

        /// <summary>
        ///     Checks for inconsistencies in the metadata returned by the service, returning an appropriate error message (null if none).
        /// </summary>
        protected static string CheckForResponseInconsistency(ContentHashListWithCacheMetadata cacheMetadata)
        {
            if (cacheMetadata != null)
            {
                if (cacheMetadata.GetEffectiveExpirationTimeUtc() == null &&
                    cacheMetadata.ContentGuarantee != ContentAvailabilityGuarantee.NoContentBackedByCache)
                {
                    return
                        "Inconsistent BuildCache service response. Null ContentHashListExpirationUtc should be iff ContentAvailabilityGuarantee.NoContentBackedByCache.";
                }
            }

            return null;
        }

        /// <summary>
        ///     Determine the ContentHashList to return based on the added value and the service response.
        /// </summary>
        protected static ContentHashList UnpackContentHashListAfterAdd(
            ContentHashList addedContentHashList, ContentHashListWithCacheMetadata cacheMetadata)
        {
            Contract.Assert(cacheMetadata != null);

            if (cacheMetadata.ContentHashListWithDeterminism.ContentHashList != null &&
                !addedContentHashList.Equals(
                    cacheMetadata.ContentHashListWithDeterminism.ContentHashList))
            {
                // The service returned a ContentHashList different from the one we tried to add, so we'll return that.
                return cacheMetadata.ContentHashListWithDeterminism.ContentHashList;
            }

            // The added value was accepted, so we return null.
            return null;
        }

        /// <summary>
        ///     Determine the Determinism to return.
        /// </summary>
        protected static CacheDeterminism UnpackDeterminism(ContentHashListWithCacheMetadata cacheMetadata, Guid cacheId)
        {
            Contract.Assert(cacheMetadata != null);

            if (cacheMetadata.ContentHashListWithDeterminism.Determinism.Guid == CacheDeterminism.Tool.Guid)
            {
                // Value is Tool-deterministic
                return CacheDeterminism.Tool;
            }

            var expirationUtc = cacheMetadata.GetEffectiveExpirationTimeUtc();
            return expirationUtc == null
                ? CacheDeterminism.None // Value is unbacked in VSTS
                : CacheDeterminism.ViaCache(cacheId, expirationUtc.Value); // Value is backed in VSTS
        }

        private void SealIfNecessaryAfterGet(OperationContext context, StrongFingerprint strongFingerprint, ContentHashListWithCacheMetadata cacheMetadata)
        {
            if (WriteThroughContentSession == null)
            {
                return;
            }

            if (cacheMetadata != null && cacheMetadata.GetEffectiveExpirationTimeUtc() == null)
            {
                // Value is unbacked in VSTS
                SealInTheBackground(context, strongFingerprint, cacheMetadata.ContentHashListWithDeterminism);
            }
        }

        /// <summary>
        ///     Queue a seal operation in the background. Attempts to upload all content to VSTS before updating the metadata as backed.
        ///     Lost races will be ignored and any failures will be logged and reported on shutdown.
        /// </summary>
        protected void SealInTheBackground(
            OperationContext context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            if (_sealUnbackedContentHashLists)
            {
                _taskTracker.Add(Task.Run(() => SealAsync(context, strongFingerprint, contentHashListWithDeterminism)));
            }
        }

        private async Task SealAsync(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            Contract.Assert(WriteThroughContentSession != null);
            Contract.Assert(contentHashListWithDeterminism.ContentHashList != null);
            try
            {
                var uploadResult = await UploadAllContentAsync(
                    context,
                    strongFingerprint.Selector.ContentHash,
                    contentHashListWithDeterminism.ContentHashList.Hashes,
                    CancellationToken.None,
                    UrgencyHint.Low).ConfigureAwait(false);
                if (uploadResult.Code == PinResult.ResultCode.ContentNotFound)
                {
                    CacheTracer.Debug(context, "Background seal unable to find all content during upload.");
                    return;
                }

                if (!uploadResult.Succeeded)
                {
                    ReportSealingError(context, $"Background seal failed during upload: error=[{uploadResult}].");
                    return;
                }

                var sealResult = await AddOrGetContentHashListAsync(
                    context, strongFingerprint, contentHashListWithDeterminism, ContentAvailabilityGuarantee.AllContentBackedByCache).ConfigureAwait(false);
                if (sealResult.Succeeded)
                {
                    CacheTracer.Debug(
                        context,
                        sealResult.ContentHashListWithDeterminism.ContentHashList == null ? $"Successfully sealed value for strongFingerprint [{strongFingerprint}]." : $"Lost the race in sealing value for strongFingerprint [{strongFingerprint}].");
                }
                else
                {
                    ReportSealingError(context, $"Background seal failed during sealing: {sealResult}");
                }
            }
            catch (Exception e)
            {
                ReportSealingError(context, $"Background seal threw exception: {e}.");
            }
        }

        private void ReportSealingError(Context context, string errorMessage, [CallerMemberName] string operation = null)
        {
            Interlocked.Increment(ref _sealingErrorCount);
            CacheTracer.Error(context, errorMessage, operation);
            if (_sealingErrorCount < MaxSealingErrorsToPrintOnShutdown)
            {
                _sealingErrorsToPrintOnShutdown.Add(errorMessage);
            }
        }

        /// <summary>
        ///     Attempt to ensure that all content is in VSTS, uploading any misses from the WriteThroughContentSession.
        /// </summary>
        private async Task<PinResult> UploadAllContentAsync(
            Context context, ContentHash selectorHash, IEnumerable<ContentHash> hashes, CancellationToken cts, UrgencyHint urgencyHint)
        {
            List<ContentHash> contentToUpload = new List<ContentHash>();

            var pinResult = await BackingContentSession.PinAsync(context, selectorHash, cts, urgencyHint).ConfigureAwait(false);
            if (pinResult.Code == PinResult.ResultCode.ContentNotFound)
            {
                contentToUpload.Add(selectorHash);
            }
            else if (!pinResult.Succeeded)
            {
                return pinResult;
            }

            foreach (var contentHash in hashes)
            {
                pinResult = await BackingContentSession.PinAsync(context, contentHash, cts, urgencyHint).ConfigureAwait(false);
                if (pinResult.Code == PinResult.ResultCode.ContentNotFound)
                {
                    contentToUpload.Add(contentHash);
                }
                else if (!pinResult.Succeeded)
                {
                    return pinResult;
                }
            }

            foreach (var contentHash in contentToUpload)
            {
                // TODO: Upload the content efficiently (in parallel and with caching of success) (bug 1365340)
                AbsolutePath tempFile = null;
                try
                {
                    tempFile = _tempDirectory.CreateRandomFileName();
                    var placeResult = await WriteThroughContentSession.PlaceFileAsync(
                        context,
                        contentHash,
                        tempFile,
                        FileAccessMode.Write,
                        FileReplacementMode.FailIfExists,
                        FileRealizationMode.Any,
                        CancellationToken.None).ConfigureAwait(false);
                    if (placeResult.Code == PlaceFileResult.ResultCode.NotPlacedContentNotFound)
                    {
                        return PinResult.ContentNotFound;
                    }
                    else if (!placeResult.Succeeded)
                    {
                        return new PinResult(placeResult);
                    }

                    var putResult = await BackingContentSession.PutFileAsync(
                        context, contentHash, tempFile, FileRealizationMode.Any, CancellationToken.None).ConfigureAwait(false);
                    if (!putResult.Succeeded)
                    {
                        return new PinResult(putResult);
                    }
                }
                finally
                {
                    if (tempFile != null)
                    {
                        try
                        {
                            File.Delete(tempFile.Path);
                        }
                        catch (Exception e)
                        {
                            CacheTracer.Warning(context, $"Error deleting temporary file at {tempFile.Path}: {e}");
                        }
                    }
                }
            }

            return PinResult.Success;
        }

        private struct IncorporationOptions
        {

        }
    }
}
