// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    ///     L3 ContentStore implemented against VSTS BlobStore
    /// </summary>
    public sealed class BackingContentStore : IContentStore
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

        private readonly IAbsFileSystem _fileSystem;
        private readonly IArtifactHttpClientFactory _artifactHttpClientFactory;
        private readonly TimeSpan _timeToKeepContent;
        private IArtifactHttpClient _artifactHttpClient;
        private readonly bool _useDedupStore;

        /// <summary>
        /// Used for BlobStore implementation only.
        /// </summary>
        private readonly bool _downloadBlobsThroughBlobStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackingContentStore"/> class.
        /// </summary>
        /// <param name="fileSystem">Filesystem used to read/write files.</param>
        /// <param name="artifactHttpClientFactory">Backing Store HTTP client factory.</param>
        /// <param name="timeToKeepContent">Minimum time-to-live for accessed content.</param>
        /// <param name="downloadBlobsThroughBlobStore">Flag for BlobStore: If enabled, gets blobs through BlobStore. If false, gets blobs from the Azure Uri.</param>
        /// <param name="useDedupStore">Determines whether or not DedupStore is used for content. Must be used in tandem with Dedup hashes.</param>
        public BackingContentStore(
            IAbsFileSystem fileSystem,
            IArtifactHttpClientFactory artifactHttpClientFactory,
            TimeSpan timeToKeepContent,
            bool downloadBlobsThroughBlobStore = false,
            bool useDedupStore = false)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(artifactHttpClientFactory != null);
            _fileSystem = fileSystem;
            _artifactHttpClientFactory = artifactHttpClientFactory;
            _timeToKeepContent = timeToKeepContent;
            _downloadBlobsThroughBlobStore = downloadBlobsThroughBlobStore;
            _useDedupStore = useDedupStore;
        }

        /// <inheritdoc />
        public async Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;

            BoolResult result;
            try
            {
                result = await _artifactHttpClientFactory.StartupAsync(context);
                if (!result.Succeeded)
                {
                    return result;
                }

                if (_useDedupStore)
                {
                    _artifactHttpClient = await _artifactHttpClientFactory.CreateDedupStoreHttpClientAsync(context).ConfigureAwait(false);
                }
                else
                {
                    _artifactHttpClient = await _artifactHttpClientFactory.CreateBlobStoreHttpClientAsync(context).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                result = new BoolResult(e);
            }

            StartupCompleted = true;
            return result;
        }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_artifactHttpClient is IBlobStoreHttpClient blobStoreHttpClient)
            {
                blobStoreHttpClient.Dispose();
            }
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            ShutdownCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(
            Context context, string name, ImplicitPin implicitPin)
        {
            if (_useDedupStore)
            {
                return new CreateSessionResult<IReadOnlyContentSession>(new DedupReadOnlyContentSession(
                    _fileSystem, name, implicitPin, _artifactHttpClient as IDedupStoreHttpClient, _timeToKeepContent));
            }

            return new CreateSessionResult<IReadOnlyContentSession>(new BlobReadOnlyContentSession(
                _fileSystem, name, implicitPin, _artifactHttpClient as IBlobStoreHttpClient, _timeToKeepContent, _downloadBlobsThroughBlobStore));
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            if (_useDedupStore)
            {
                return new CreateSessionResult<IContentSession>(new DedupContentSession(
                    context, _fileSystem, name, implicitPin, _artifactHttpClient as IDedupStoreHttpClient, _timeToKeepContent));
            }

            return new CreateSessionResult<IContentSession>(new BlobContentSession(
                _fileSystem, name, implicitPin, _artifactHttpClient as IBlobStoreHttpClient, _timeToKeepContent, _downloadBlobsThroughBlobStore));
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context) => Task.FromResult(new GetStatsResult(new CounterSet()));
    }
}
