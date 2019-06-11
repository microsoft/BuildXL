// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Vsts.Adapters;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    ///     L3 ICache implemented against the VSTS BuildCache Service.
    /// </summary>
    public sealed class BuildCacheCache : ICache, IMemoizationStore
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly string _cacheNamespace;
        private readonly IBuildCacheHttpClientFactory _buildCacheHttpClientFactory;
        private readonly IContentStore _backingContentStore;
        private readonly TimeSpan _minimumTimeToKeepContentHashLists;
        private readonly TimeSpan _rangeOfTimeToKeepContentHashLists;
        private readonly int _maxFingerprintSelectorsToFetch;
        private readonly IContentStore _writeThroughContentStore;
        private readonly bool _sealUnbackedContentHashLists;
        private readonly bool _useBlobContentHashLists;
        private readonly bool _fingerprintIncorporationEnabled;
        private readonly int _maxDegreeOfParallelismForIncorporateRequests;
        private readonly int _maxFingerprintsPerIncorporateRequest;
        private readonly BuildCacheCacheTracer _tracer;
        private ContentHashListAdapterFactory _contentHashListAdapterFactory;
        private readonly bool _overrideUnixFileAccessMode;

        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BuildCacheCache"/> class.
        /// </summary>
        /// <param name="fileSystem">Filesystem used to read/write files.</param>
        /// <param name="cacheNamespace">the namespace of the cache that is communicated with.</param>
        /// <param name="buildCacheHttpClientFactory">Factory for creatign a backing BuildCache http client.</param>
        /// <param name="backingContentStoreHttpClientFactory">Factory for creating a backing store http client.</param>
        /// <param name="maxFingerprintSelectorsToFetch">Maximum number of selectors to enumerate.</param>
        /// <param name="timeToKeepUnreferencedContent">Initial time-to-live for unreferenced content.</param>
        /// <param name="minimumTimeToKeepContentHashLists">Minimum time-to-live for created or referenced ContentHashLists.</param>
        /// <param name="rangeOfTimeToKeepContentHashLists">Range of time beyond the minimum for the time-to-live of created or referenced ContentHashLists.</param>
        /// <param name="logger">A logger for tracing.</param>
        /// <param name="fingerprintIncorporationEnabled">Feature flag to enable fingerprints incorporation on shutdown</param>
        /// <param name="maxDegreeOfParallelismForIncorporateRequests">Throttle the number of fingerprints chunks sent in parallel</param>
        /// <param name="maxFingerprintsPerIncorporateRequest">Max fingerprints allowed per chunk</param>
        /// <param name="writeThroughContentStoreFunc">Optional write-through store to allow writing-behind to BlobStore</param>
        /// <param name="sealUnbackedContentHashLists">If true, the client will attempt to seal any unbacked ContentHashLists that it sees.</param>
        /// <param name="useBlobContentHashLists">use blob based content hash lists.</param>
        /// <param name="downloadBlobsThroughBlobStore">If true, gets blobs through BlobStore. If false, gets blobs from the Azure Uri.</param>
        /// <param name="useDedupStore">If true, gets content through DedupStore. If false, gets content from BlobStore.</param>
        /// <param name="overrideUnixFileAccessMode">If true, overrides default Unix file access modes.</param>
        public BuildCacheCache(
            IAbsFileSystem fileSystem,
            string cacheNamespace,
            IBuildCacheHttpClientFactory buildCacheHttpClientFactory,
            IArtifactHttpClientFactory backingContentStoreHttpClientFactory,
            int maxFingerprintSelectorsToFetch,
            TimeSpan timeToKeepUnreferencedContent,
            TimeSpan minimumTimeToKeepContentHashLists,
            TimeSpan rangeOfTimeToKeepContentHashLists,
            ILogger logger,
            bool fingerprintIncorporationEnabled,
            int maxDegreeOfParallelismForIncorporateRequests,
            int maxFingerprintsPerIncorporateRequest,
            Func<IContentStore> writeThroughContentStoreFunc = null,
            bool sealUnbackedContentHashLists = false,
            bool useBlobContentHashLists = false,
            bool downloadBlobsThroughBlobStore = false,
            bool useDedupStore = false,
            bool overrideUnixFileAccessMode = false)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(buildCacheHttpClientFactory != null);
            Contract.Requires(backingContentStoreHttpClientFactory != null);

            _fileSystem = fileSystem;
            _cacheNamespace = cacheNamespace;
            _buildCacheHttpClientFactory = buildCacheHttpClientFactory;
            _tracer = new BuildCacheCacheTracer(logger, nameof(BuildCacheCache));

            _backingContentStore = new BackingContentStore(
                fileSystem, backingContentStoreHttpClientFactory, timeToKeepUnreferencedContent, downloadBlobsThroughBlobStore, useDedupStore);

            if (useDedupStore)
            {
                // Guaranteed content is only available for BlobSessions. (bug 144396)
                _sealUnbackedContentHashLists = false;

                // BlobBuildCacheHttpClient is incompatible with Dedup hashes. (bug 1458510)
                _useBlobContentHashLists = false;
            }
            else
            {
                _sealUnbackedContentHashLists = sealUnbackedContentHashLists;
                _useBlobContentHashLists = useBlobContentHashLists;
            }

            _maxFingerprintSelectorsToFetch = maxFingerprintSelectorsToFetch;
            _minimumTimeToKeepContentHashLists = minimumTimeToKeepContentHashLists;
            _rangeOfTimeToKeepContentHashLists = rangeOfTimeToKeepContentHashLists;

            if (writeThroughContentStoreFunc != null)
            {
                _writeThroughContentStore = writeThroughContentStoreFunc();
                Contract.Assert(_writeThroughContentStore != null);
            }

            _fingerprintIncorporationEnabled = fingerprintIncorporationEnabled;
            _maxDegreeOfParallelismForIncorporateRequests = maxDegreeOfParallelismForIncorporateRequests;
            _maxFingerprintsPerIncorporateRequest = maxFingerprintsPerIncorporateRequest;
            _overrideUnixFileAccessMode = overrideUnixFileAccessMode;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _contentHashListAdapterFactory?.Dispose();
                _writeThroughContentStore?.Dispose();
                _backingContentStore.Dispose();
            }

            _disposed = true;
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            return ShutdownCall<BuildCacheCacheTracer>.RunAsync(_tracer, context, async () =>
            {
                var statsResult = await GetStatsInternalAsync(context).ConfigureAwait(false);
                if (statsResult.Succeeded)
                {
                    context.Debug("BuildCacheCache Stats:");
                    statsResult.CounterSet.LogOrderedNameValuePairs(s => _tracer.Debug(context, s));
                }
                else
                {
                    context.Debug($"Getting stats failed: [{statsResult}]");
                }

                var backingContentStoreTask = Task.Run(async () => await _backingContentStore.ShutdownAsync(context).ConfigureAwait(false));
                var writeThroughContentStoreResult = _writeThroughContentStore != null
                    ? await _writeThroughContentStore.ShutdownAsync(context)
                    : BoolResult.Success;
                var backingContentStoreResult = await backingContentStoreTask.ConfigureAwait(false);

                BoolResult result;
                if (backingContentStoreResult.Succeeded && writeThroughContentStoreResult.Succeeded)
                {
                    result = BoolResult.Success;
                }
                else
                {
                    var sb = new StringBuilder();
                    if (!backingContentStoreResult.Succeeded)
                    {
                        sb.Append($"Backing content store shutdown failed, error=[{backingContentStoreResult}]");
                    }

                    if (!writeThroughContentStoreResult.Succeeded)
                    {
                        sb.Append($"Write-through content store shutdown failed, error=[{writeThroughContentStoreResult}]");
                    }

                    result = new BoolResult(sb.ToString());
                }

                ShutdownCompleted = true;
                return result;
            });
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            return StartupCall<BuildCacheCacheTracer>.RunAsync(_tracer, context, async () =>
            {
                BoolResult result;
                _contentHashListAdapterFactory = await ContentHashListAdapterFactory.CreateAsync(
                    context, _buildCacheHttpClientFactory, _useBlobContentHashLists);
                Id =
                    await _contentHashListAdapterFactory.BuildCacheHttpClient.GetBuildCacheServiceDeterminism(_cacheNamespace)
                        .ConfigureAwait(false);

                var backingContentStoreTask = Task.Run(async () => await _backingContentStore.StartupAsync(context).ConfigureAwait(false));
                var writeThroughContentStoreResult = _writeThroughContentStore != null
                    ? await _writeThroughContentStore.StartupAsync(context).ConfigureAwait(false)
                    : BoolResult.Success;
                var backingContentStoreResult = await backingContentStoreTask.ConfigureAwait(false);

                if (backingContentStoreResult.Succeeded && writeThroughContentStoreResult.Succeeded)
                {
                    result = BoolResult.Success;
                }
                else
                {
                    var sb = new StringBuilder();
                    if (backingContentStoreResult.Succeeded)
                    {
                        var r = await _backingContentStore.ShutdownAsync(context).ConfigureAwait(false);
                        if (!r.Succeeded)
                        {
                            sb.Append($"Backing content store shutdown failed, error=[{r}]");
                        }
                    }
                    else
                    {
                        sb.Append($"Backing content store startup failed, error=[{backingContentStoreResult}]");
                    }

                    if (writeThroughContentStoreResult.Succeeded)
                    {
                        var r = _writeThroughContentStore != null
                            ? await _writeThroughContentStore.ShutdownAsync(context).ConfigureAwait(false)
                            : BoolResult.Success;
                        if (!r.Succeeded)
                        {
                            sb.Append(sb.Length > 0 ? ", " : string.Empty);
                            sb.Append($"Write-through content store shutdown failed, error=[{r}]");
                        }
                    }
                    else
                    {
                        sb.Append(sb.Length > 0 ? ", " : string.Empty);
                        sb.Append($"Write-through content store startup failed, error=[{writeThroughContentStoreResult}]");
                    }

                    result = new BoolResult(sb.ToString());
                }

                StartupCompleted = true;
                return result;
            });
        }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return Tracing.CreateReadOnlySessionCall.Run(_tracer, context, name, () =>
            {
                var backingContentSessionResult = _backingContentStore.CreateSession(context, name, implicitPin);
                if (!backingContentSessionResult.Succeeded)
                {
                    return new CreateSessionResult<IReadOnlyCacheSession>(backingContentSessionResult);
                }

                IContentSession writeThroughContentSession = null;
                if (_writeThroughContentStore != null)
                {
                    var writeThroughContentSessionResult = _writeThroughContentStore.CreateSession(context, name, implicitPin);
                    if (!writeThroughContentSessionResult.Succeeded)
                    {
                        return new CreateSessionResult<IReadOnlyCacheSession>(writeThroughContentSessionResult);
                    }

                    writeThroughContentSession = writeThroughContentSessionResult.Session;
                }

                return new CreateSessionResult<IReadOnlyCacheSession>(
                    new BuildCacheReadOnlySession(
                        _fileSystem,
                        name,
                        implicitPin,
                        _cacheNamespace,
                        Id,
                        _contentHashListAdapterFactory.Create(backingContentSessionResult.Session),
                        backingContentSessionResult.Session,
                        _maxFingerprintSelectorsToFetch,
                        _minimumTimeToKeepContentHashLists,
                        _rangeOfTimeToKeepContentHashLists,
                        _fingerprintIncorporationEnabled,
                        _maxDegreeOfParallelismForIncorporateRequests,
                        _maxFingerprintsPerIncorporateRequest,
                        writeThroughContentSession,
                        _sealUnbackedContentHashLists,
                        _overrideUnixFileAccessMode,
                        _tracer));
            });
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return Tracing.CreateSessionCall.Run(_tracer, context, name, () =>
            {
                var backingContentSessionResult = _backingContentStore.CreateSession(context, name, implicitPin);
                if (!backingContentSessionResult.Succeeded)
                {
                    return new CreateSessionResult<ICacheSession>(backingContentSessionResult);
                }

                IContentSession writeThroughContentSession = null;
                if (_writeThroughContentStore != null)
                {
                    var writeThroughContentSessionResult = _writeThroughContentStore.CreateSession(context, name, implicitPin);
                    if (!writeThroughContentSessionResult.Succeeded)
                    {
                        return new CreateSessionResult<ICacheSession>(writeThroughContentSessionResult);
                    }

                    writeThroughContentSession = writeThroughContentSessionResult.Session;
                }

                return new CreateSessionResult<ICacheSession>(
                    new BuildCacheSession(
                        _fileSystem,
                        name,
                        implicitPin,
                        _cacheNamespace,
                        Id,
                        _contentHashListAdapterFactory.Create(backingContentSessionResult.Session),
                        backingContentSessionResult.Session,
                        _maxFingerprintSelectorsToFetch,
                        _minimumTimeToKeepContentHashLists,
                        _rangeOfTimeToKeepContentHashLists,
                        _fingerprintIncorporationEnabled,
                        _maxDegreeOfParallelismForIncorporateRequests,
                        _maxFingerprintsPerIncorporateRequest,
                        writeThroughContentSession,
                        _sealUnbackedContentHashLists,
                        _overrideUnixFileAccessMode,
                        _tracer));
            });
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<BuildCacheCacheTracer>.RunAsync(_tracer, new OperationContext(context), () => GetStatsInternalAsync(context));
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        private async Task<GetStatsResult> GetStatsInternalAsync(Context context)
        {
            try
            {
                var aggregateStats = new CounterSet();
                var cachestats = _tracer.GetCounters();

                aggregateStats.Merge(cachestats);
                if (_writeThroughContentStore != null)
                {
                    GetStatsResult writeThrouStoreStats = await _writeThroughContentStore.GetStatsAsync(context);
                    if (writeThrouStoreStats.Succeeded)
                    {
                        aggregateStats.Merge(writeThrouStoreStats.CounterSet, "WriteThroughStore.");
                    }
                }

                return new GetStatsResult(aggregateStats);
            }
            catch (Exception ex)
            {
                return new GetStatsResult(ex);
            }
        }

        /// <inheritdoc />
        public Guid Id { get; private set; }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            return AsyncEnumerable.Empty<StructResult<StrongFingerprint>>();
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyMemoizationSession> CreateReadOnlySession(Context context, string name)
        {
            var result = CreateReadOnlySession(context, name, ImplicitPin.None);

            return result.Succeeded
                ? new CreateSessionResult<IReadOnlyMemoizationSession>(result.Session)
                : new CreateSessionResult<IReadOnlyMemoizationSession>(result);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name)
        {
            var result = CreateSession(context, name, ImplicitPin.None);

            return result.Succeeded
                ? new CreateSessionResult<IMemoizationSession>(result.Session)
                : new CreateSessionResult<IMemoizationSession>(result);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name, IContentSession contentSession)
        {
            throw new NotImplementedException();
        }
    }
}
