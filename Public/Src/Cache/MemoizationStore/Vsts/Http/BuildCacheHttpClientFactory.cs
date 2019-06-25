// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common;

namespace BuildXL.Cache.MemoizationStore.Vsts.Http
{
    /// <summary>
    /// Factory class for creating HTTP clients that can communicate with a VSTS Build Cache service.
    /// </summary>
    public class BuildCacheHttpClientFactory : IBuildCacheHttpClientFactory
    {
        private readonly Uri _buildCacheBaseUri;
        private readonly VssCredentialsFactory _vssCredentialsFactory;
        private readonly TimeSpan _httpSendTimeout;
        private readonly bool _useAad;

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildCacheHttpClientFactory"/> class.
        /// </summary>
        public BuildCacheHttpClientFactory(Uri buildCacheBaseUri, VssCredentialsFactory vssCredentialsFactory, TimeSpan httpSendTimeout, bool useAad = true)
        {
            _buildCacheBaseUri = buildCacheBaseUri;
            _vssCredentialsFactory = vssCredentialsFactory;
            _httpSendTimeout = httpSendTimeout;
            _useAad = useAad;
        }

        /// <summary>
        /// Creates an http client that can communicate with a VSTS Build Cache Service.
        /// </summary>
        public async Task<IBuildCacheHttpClient> CreateBuildCacheHttpClientAsync(Context context)
        {
            RetryPolicy retryPolicy = new RetryPolicy<AuthorizationErrorDetectionStrategy>(RetryStrategy.DefaultExponential);
            var creds = await retryPolicy.ExecuteAsync(() => _vssCredentialsFactory.CreateVssCredentialsAsync(_buildCacheBaseUri, _useAad)).ConfigureAwait(false);

            var httpClientFactory = new ArtifactHttpClientFactory(
                creds,
                _httpSendTimeout,
                tracer: new AppTraceSourceContextAdapter(context, "BuildCacheHttpClientFactory", SourceLevels.All),
                verifyConnectionCancellationToken: CancellationToken.None); // TODO: Pipe down cancellation support (bug 1365340)
            IBuildCacheHttpClient client =
                httpClientFactory.CreateVssHttpClient<IArtifactBuildCacheHttpClient, ItemBuildCacheHttpClient>(_buildCacheBaseUri);
            await ArtifactHttpClientErrorDetectionStrategy.ExecuteAsync(
                context,
                "VerifyBuildCacheHttpClientConnection",
                () => httpClientFactory.VerifyConnectionAsync(client as IArtifactHttpClient),
                CancellationToken.None).ConfigureAwait(false);
            context.TraceMessage(
                Severity.Debug, $"Verified connection to {_buildCacheBaseUri} with SessionId=[{httpClientFactory.ClientSettings.SessionId}]");
            return client;
        }

        /// <summary>
        /// Creates an http client that can communicate with a VSTS Build Cache Service.
        /// </summary>
        public async Task<IBlobBuildCacheHttpClient> CreateBlobBuildCacheHttpClientAsync(Context context)
        {
            RetryPolicy authRetryPolicy = new RetryPolicy<AuthorizationErrorDetectionStrategy>(RetryStrategy.DefaultExponential);
            var creds = await authRetryPolicy.ExecuteAsync(() => _vssCredentialsFactory.CreateVssCredentialsAsync(_buildCacheBaseUri, _useAad)).ConfigureAwait(false);

            var httpClientFactory = new ArtifactHttpClientFactory(
                creds,
                _httpSendTimeout,
                tracer: new AppTraceSourceContextAdapter(context, "BuildCacheHttpClientFactory", SourceLevels.All),
                verifyConnectionCancellationToken: CancellationToken.None); // TODO: Pipe down cancellation support (bug 1365340)
            IBlobBuildCacheHttpClient client =
                httpClientFactory.CreateVssHttpClient<IArtifactBlobBuildCacheHttpClient, BlobBuildCacheHttpClient>(_buildCacheBaseUri);
            await ArtifactHttpClientErrorDetectionStrategy.ExecuteAsync(
                context,
                "VerifyBlobBuildCacheHttpClientConnection",
                () => httpClientFactory.VerifyConnectionAsync(client as IArtifactHttpClient),
                CancellationToken.None).ConfigureAwait(false);
            context.TraceMessage(
                Severity.Debug, $"Verified connection to {_buildCacheBaseUri} with SessionId=[{httpClientFactory.ClientSettings.SessionId}]");
            return client;
        }
    }
}
