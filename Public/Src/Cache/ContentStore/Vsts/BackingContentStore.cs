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
        private readonly BackingContentStoreConfiguration _configuration;
        private IArtifactHttpClient _artifactHttpClient;

        /// <nodoc />
        public BackingContentStore(BackingContentStoreConfiguration configuration)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(configuration.FileSystem != null);
            Contract.Requires(configuration.ArtifactHttpClientFactory != null);

            _configuration = configuration;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            BoolResult result;
            try
            {
                result = await _configuration.ArtifactHttpClientFactory.StartupAsync(context);
                if (!result.Succeeded)
                {
                    return result;
                }

                _artifactHttpClient = _configuration.UseDedupStore
                    ? (IArtifactHttpClient)await _configuration.ArtifactHttpClientFactory.CreateDedupStoreHttpClientAsync(context).ConfigureAwait(false)
                    : await _configuration.ArtifactHttpClientFactory.CreateBlobStoreHttpClientAsync(context).ConfigureAwait(false);
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
            if (_configuration.UseDedupStore)
            {
                // TODO: Change DedupContentSession to use BackingContentStoreConfiguration
                return new CreateSessionResult<IBackingContentSession>(new DedupContentSession(
                    context, _configuration.FileSystem, name, implicitPin, _artifactHttpClient as IDedupStoreHttpClient, _configuration.TimeToKeepContent, _configuration.PinInlineThreshold, _configuration.IgnorePinThreshold, _sessionCounterTracker.AddOrGetChildCounterTracker("Dedup.")));
            }

            return new CreateSessionResult<IBackingContentSession>(new BlobContentSession(
                _configuration, name, implicitPin, _artifactHttpClient as IBlobStoreHttpClient, _sessionCounterTracker.AddOrGetChildCounterTracker("Blob.")));
        }

        /// <nodoc />
        public GetStatsResult GetStats() => new GetStatsResult(_sessionCounterTracker.ToCounterSet());
    }
}
