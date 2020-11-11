// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <inheritdoc />
    public class BackingContentStoreHttpClientFactory : IArtifactHttpClientFactory, IStartup<BoolResult>
    {
        private readonly Uri _backingStoreBaseUri;
        private readonly VssCredentialsFactory _vssCredentialsFactory;
        private readonly TimeSpan _httpSendTimeout;
        private readonly bool _useAad;
        private ArtifactHttpClientFactory _httpClientFactory;
        private readonly IDomainId _domain;

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BackingContentStoreHttpClientFactory"/> class.
        /// </summary>
        /// <param name="backingStoreBaseUri">The base VSTS Uri to the dedup service.</param>
        /// <param name="vssCredentialsFactory">A provider for credentials connecting to the factory.</param>
        /// <param name="httpSendTimeout">How long to wait for a response after sending an http request before timing out.</param>
        /// <param name="domain">Domain ID to use against the backing store.</param>
        /// <param name="useAad">Whether or not to use production AAD for authentication.</param>
        public BackingContentStoreHttpClientFactory(Uri backingStoreBaseUri, VssCredentialsFactory vssCredentialsFactory, TimeSpan httpSendTimeout, IDomainId domain, bool useAad = true)
        {
            _backingStoreBaseUri = backingStoreBaseUri;
            _vssCredentialsFactory = vssCredentialsFactory;
            _httpSendTimeout = httpSendTimeout;
            _useAad = useAad;
            _domain = domain;
        }

        /// <inheritdoc />
        public async Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            IRetryPolicy retryPolicy = RetryPolicyFactory.GetExponentialPolicy(AuthorizationErrorDetectionStrategy.IsTransient);

            try
            {
                var creds = await retryPolicy.ExecuteAsync(() => _vssCredentialsFactory.CreateVssCredentialsAsync(_backingStoreBaseUri, _useAad), CancellationToken.None).ConfigureAwait(false);
                _httpClientFactory = new ArtifactHttpClientFactory(
                    creds,
                    _httpSendTimeout,
                    tracer: new AppTraceSourceContextAdapter(context, nameof(BackingContentStoreHttpClientFactory), SourceLevels.All),
                    verifyConnectionCancellationToken: CancellationToken.None); // TODO: Pipe down cancellation support (bug 1365340)
                StartupCompleted = true;
                return BoolResult.Success;
            }
            catch (Exception ex)
            {
                return new BoolResult(ex);
            }
        }

        /// <inheritdoc />
        public async Task<IBlobStoreHttpClient> CreateBlobStoreHttpClientAsync(Context context)
        {
            if (_domain.Equals(WellKnownDomainIds.DefaultDomainId))
            {
                var client = BlobStoreHttpClientFactory.GetClient(_backingStoreBaseUri, _httpClientFactory);
                await ArtifactHttpClientErrorDetectionStrategy.ExecuteAsync(
                    context,
                    "VerifyBlobStoreHttpClientConnection",
                    () => _httpClientFactory.VerifyConnectionAsync(client),
                    CancellationToken.None).ConfigureAwait(false);
                context.TraceMessage(
                    Severity.Debug, $"Verified connection to {_backingStoreBaseUri} with SessionId=[{_httpClientFactory.ClientSettings.SessionId}], Domain=[Default]");
                return client;
            }

            return await CreateDomainBlobStoreHttpClientAsync(context);
        }

        /// <inheritdoc />
        public async Task<IDedupStoreHttpClient> CreateDedupStoreHttpClientAsync(Context context)
        {
            if (_domain.Equals(WellKnownDomainIds.DefaultDomainId))
            {
                var client = DedupStoreHttpClientFactory.GetClient(_backingStoreBaseUri, _httpClientFactory);
                await ArtifactHttpClientErrorDetectionStrategy.ExecuteAsync(
                    context,
                    "VerifyDedupStoreHttpClientConnection",
                    () => _httpClientFactory.VerifyConnectionAsync(client),
                    CancellationToken.None).ConfigureAwait(false);
                context.TraceMessage(
                    Severity.Debug, $"Verified connection to {_backingStoreBaseUri} with SessionId=[{_httpClientFactory.ClientSettings.SessionId}], Domain=[Default]");
                return client;
            }

            return await CreateDomainDedupStoreHttpClientAsync(context);
        }

        private async Task<IBlobStoreHttpClient> CreateDomainBlobStoreHttpClientAsync(Context context)
        {
            var client = BlobStoreHttpClientFactory.GetDomainClient(_backingStoreBaseUri, _httpClientFactory);
            await ArtifactHttpClientErrorDetectionStrategy.ExecuteAsync(
                context,
                "VerifyDomainBlobStoreHttpClientConnection",
                () => _httpClientFactory.VerifyConnectionAsync(client),
                CancellationToken.None).ConfigureAwait(false);
            context.TraceMessage(
                Severity.Debug, $"Verified connection to {_backingStoreBaseUri} with SessionId=[{_httpClientFactory.ClientSettings.SessionId}], Domain=[{_domain.Serialize()}]");

            return new DomainBlobHttpClientWrapper(_domain, client);
        }

        private async Task<IDedupStoreHttpClient> CreateDomainDedupStoreHttpClientAsync(Context context)
        {
            var client = DedupStoreHttpClientFactory.GetDomainClient(_backingStoreBaseUri, _httpClientFactory);
            await ArtifactHttpClientErrorDetectionStrategy.ExecuteAsync(
                context,
                "VerifyDomainBlobStoreHttpClientConnection",
                () => _httpClientFactory.VerifyConnectionAsync(client),
                CancellationToken.None).ConfigureAwait(false);
            context.TraceMessage(
                Severity.Debug, $"Verified connection to {_backingStoreBaseUri} with SessionId=[{_httpClientFactory.ClientSettings.SessionId}], Domain=[{_domain.Serialize()}]");

            return new DomainHttpClientWrapper(_domain, client);
        }
    }
}
