// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.MemoizationStore.Vsts.Http;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    /// A concrete implementation of an http client communicating with the VSTS build cache service.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
    [ResourceArea(BuildCacheResourceIds.BuildCacheArea)]
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class ItemBuildCacheHttpClient : VssHttpClientBase, IArtifactBuildCacheHttpClient, IAdminBuildCacheHttpClient
    {
        private static readonly Dictionary<string, Type> TranslatedExceptionsValue = new Dictionary<string, Type>
            {
                { "ContentBagNotFoundException", typeof(ContentBagNotFoundException) }
            };

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemBuildCacheHttpClient"/> class.
        /// </summary>
        public ItemBuildCacheHttpClient(
            Uri baseUrl,
            VssCredentials credentials)
            : base(baseUrl, credentials)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemBuildCacheHttpClient"/> class.
        /// </summary>
        public ItemBuildCacheHttpClient(
            Uri baseUrl,
            VssCredentials credentials,
            params DelegatingHandler[] handlers)
            : base(baseUrl, credentials, handlers)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemBuildCacheHttpClient"/> class.
        /// </summary>
        public ItemBuildCacheHttpClient(
            Uri baseUrl,
            VssCredentials credentials,
            VssHttpRequestSettings requestSettings,
            params DelegatingHandler[] handlers)
            : base(baseUrl, credentials, requestSettings, handlers)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemBuildCacheHttpClient"/> class.
        /// </summary>
        public ItemBuildCacheHttpClient(
            Uri baseUrl,
            HttpMessageHandler pipeline,
            bool disposeHandler)
            : base(baseUrl, pipeline, disposeHandler)
        {
        }

        /// <summary>
        /// Gets the exceptions for errors
        /// </summary>
        protected override IDictionary<string, Type> TranslatedExceptions => TranslatedExceptionsValue;

        /// <inheritdoc />
        public async Task<Guid> ResetBuildCacheServiceDeterminism(string cacheNamespace, Guid existingCacheDeterminism)
        {
            CacheDeterminismResponse determinismGuid =
                await PostAsync<ResetCacheDeterminismRequest, CacheDeterminismResponse>(
                    new ResetCacheDeterminismRequest(existingCacheDeterminism),
                    BuildCacheResourceIds.CacheDeterminismGuidResourceId);

            return determinismGuid.CacheDeterminism;
        }

        /// <inheritdoc />
        public Task GetOptionsAsync(CancellationToken cancellationToken)
        {
            // ReSharper disable once MethodSupportsCancellation
            return SendAsync(HttpMethod.Options, BuildCacheResourceIds.ContentHashListResourceId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<ContentHashListResponse> GetContentHashListAsync(
            string cacheNamespace,
            StrongFingerprint strongFingerprint)
        {
            var routeValues = new
            {
                cacheNamespace,
                weakFingerprint = strongFingerprint.WeakFingerprint.ToHex(),
                selectorContentHash = strongFingerprint.Selector.ContentHash.ToHex(),
                selectorOutput = strongFingerprint.Selector.Output?.ToHex() ?? BuildCacheResourceIds.NoneSelectorOutput
            };
            return GetAsync<ContentHashListResponse>(
                BuildCacheResourceIds.ContentHashListResourceId,
                routeValues);
        }

        /// <inheritdoc />
        public Task<ContentHashListResponse> AddContentHashListAsync(
            string cacheNamespace,
            StrongFingerprint strongFingerprint,
            ContentHashListWithCacheMetadata contentHashList)
        {
            var addContentHashListRequest = new AddContentHashListRequest(contentHashList);

            var contentHashListParameters = new
            {
                cacheNamespace,
                weakFingerprint = strongFingerprint.WeakFingerprint.ToHex(),
                selectorContentHash = strongFingerprint.Selector.ContentHash.ToHex(),
                selectorOutput = strongFingerprint.Selector.Output?.ToHex() ?? BuildCacheResourceIds.NoneSelectorOutput
            };

            return PostAsync<AddContentHashListRequest, ContentHashListResponse>(
                addContentHashListRequest,
                BuildCacheResourceIds.ContentHashListResourceId,
                contentHashListParameters);
        }

        /// <inheritdoc />
        public Task<SelectorsResponse> GetSelectors(
            string cacheNamespace,
            Fingerprint weakFingerprint,
            int maxSelectorsToFetch,
            int maxContentBagsToFetch)
        {
            var queryParameters = new Dictionary<string, string>
            {
                { "maxSelectorsToFetch", maxSelectorsToFetch.ToString() },
                { "maxContentHashListsToFetch", maxContentBagsToFetch.ToString() }
            };
            return GetSelectorsInternal(cacheNamespace, weakFingerprint, queryParameters);
        }

        /// <inheritdoc />
        public Task<SelectorsResponse> GetSelectors(
            string cacheNamespace,
            Fingerprint weakFingerprint,
            int maxSelectorsToFetch)
        {
            var queryParameters = new Dictionary<string, string>
            {
                { "maxSelectorsToFetch", maxSelectorsToFetch.ToString() }
            };
            return GetSelectorsInternal(cacheNamespace, weakFingerprint, queryParameters);
        }

        /// <inheritdoc />
        public Task<SelectorsResponse> GetSelectors(string cacheNamespace, Fingerprint weakFingerprint)
        {
            var queryParameters = new Dictionary<string, string>();
            return GetSelectorsInternal(cacheNamespace, weakFingerprint, queryParameters);
        }

        /// <inheritdoc />
        public Task IncorporateStrongFingerprints(
            string cacheNamespace,
            IncorporateStrongFingerprintsRequest incorporateRequest)
        {
            return PutAsync(
                incorporateRequest,
                BuildCacheResourceIds.IncorporateStrongFingerprintsResourceId,
                new {cacheNamespace });
        }

        /// <inheritdoc />
        public async Task<Guid> GetBuildCacheServiceDeterminism(string cacheNamespace)
        {
            CacheDeterminismResponse cacheDeterminismResponse = await GetAsync<CacheDeterminismResponse>(
                BuildCacheResourceIds.CacheDeterminismGuidResourceId,
                new {cacheNamespace });
            return cacheDeterminismResponse.CacheDeterminism;
        }

        private Task<SelectorsResponse> GetSelectorsInternal(
            string cacheNamespace,
            Fingerprint weakFingerprint,
            Dictionary<string, string> queryParameters)
        {
            return GetAsync<SelectorsResponse>(
                BuildCacheResourceIds.SelectorResourceId,
                new {cacheNamespace, weakFingerprint = weakFingerprint.ToHex() },
                null,
                queryParameters);
        }

        private IAppTraceSource _tracer;

        /// <inheritdoc />
        public void SetTracer(IAppTraceSource tracer)
        {
            if (_tracer != null)
            {
                throw new InvalidOperationException($"{nameof(SetTracer)} was already called earlier. In order to preserve thread safety it cannot be changed.");
            }

            _tracer = tracer;
        }
    }
}
