// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    ///     L3 ContentStore implemented against VSTS BlobStore
    /// </summary>
    public sealed class BackingContentStore : StartupShutdownBase
    {
        /// <nodoc />
        public enum SessionCounters
        {
            /// <summary>
            /// Pin request had to be made to a remote VSTS store.
            /// </summary>
            PinSatisfiedFromRemote,

            /// <summary>
            /// Pin was satisfied without reaching VSTS based on existing cached data.
            /// </summary>
            PinSatisfiedInMemory
        }

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BackingContentStore));

        private readonly CounterTracker _sessionCounterTracker = new CounterTracker();
        private readonly IAbsFileSystem _fileSystem;
        private readonly IArtifactHttpClientFactory _artifactHttpClientFactory;
        private readonly TimeSpan _timeToKeepContent;
        private readonly TimeSpan _pinInlineThreshold;
        private readonly TimeSpan _ignorePinThreshold;
        private IArtifactHttpClient _artifactHttpClient;
        private readonly bool _useDedupStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackingContentStore"/> class.
        /// </summary>
        /// <param name="fileSystem">Filesystem used to read/write files.</param>
        /// <param name="artifactHttpClientFactory">Backing Store HTTP client factory.</param>
        /// <param name="timeToKeepContent">Minimum time-to-live for accessed content.</param>
        /// <param name="pinInlineThreshold">Maximum time-to-live to inline pin calls.</param>
        /// <param name="ignorePinThreshold">Minimum time-to-live to ignore pin calls.</param>
        /// <param name="useDedupStore">Determines whether or not DedupStore is used for content. Must be used in tandem with Dedup hashes.</param>
        public BackingContentStore(
            IAbsFileSystem fileSystem,
            IArtifactHttpClientFactory artifactHttpClientFactory,
            TimeSpan timeToKeepContent,
            TimeSpan pinInlineThreshold,
            TimeSpan ignorePinThreshold,
            bool useDedupStore)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(artifactHttpClientFactory != null);
            _fileSystem = fileSystem;
            _artifactHttpClientFactory = artifactHttpClientFactory;
            _timeToKeepContent = timeToKeepContent;
            _pinInlineThreshold = pinInlineThreshold;
            _ignorePinThreshold = ignorePinThreshold;
            _useDedupStore = useDedupStore;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            BoolResult result;
            try
            {
                result = await _artifactHttpClientFactory.StartupAsync(context);
                if (!result.Succeeded)
                {
                    return result;
                }

                _artifactHttpClient = _useDedupStore
                    ? (IArtifactHttpClient)await _artifactHttpClientFactory.CreateDedupStoreHttpClientAsync(context).ConfigureAwait(false)
                    : await _artifactHttpClientFactory.CreateBlobStoreHttpClientAsync(context).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                result = new BoolResult(e);
            }

            return result;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            if (_artifactHttpClient is IBlobStoreHttpClient blobStoreHttpClient)
            {
                blobStoreHttpClient.Dispose();
            }
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CreateSessionResult<IBackingContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            if (_useDedupStore)
            {
                return new CreateSessionResult<IBackingContentSession>(new DedupContentSession(
                    context, _fileSystem, name, implicitPin, _artifactHttpClient as IDedupStoreHttpClient, _timeToKeepContent, _pinInlineThreshold, _ignorePinThreshold, _sessionCounterTracker.AddOrGetChildCounterTracker("Dedup.")));
            }

            return new CreateSessionResult<IBackingContentSession>(new BlobContentSession(
                _fileSystem, name, implicitPin, _artifactHttpClient as IBlobStoreHttpClient, _timeToKeepContent, _sessionCounterTracker.AddOrGetChildCounterTracker("Blob.")));
        }

        /// <nodoc />
        public GetStatsResult GetStats() => new GetStatsResult(_sessionCounterTracker.ToCounterSet());
    }
}
