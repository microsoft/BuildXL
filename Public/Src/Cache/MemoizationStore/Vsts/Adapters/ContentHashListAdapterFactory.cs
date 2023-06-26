// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Vsts.Adapters
{
    /// <summary>
    /// A factory class for creating contenthashlistadapters
    /// </summary>
    public sealed class ContentHashListAdapterFactory : IDisposable
    {
        private ContentHashListAdapterFactory(IBuildCacheHttpClientCommon buildCacheHttpClient)
        {
            BuildCacheHttpClient = buildCacheHttpClient;
        }

        /// <summary>
        /// Gets a BuildCacheHttpClient
        /// </summary>
        public IBuildCacheHttpClientCommon BuildCacheHttpClient { get; }

        /// <summary>
        /// Creates an instance of the ContentHashListAdapterFactory class.
        /// </summary>
        public static async Task<ContentHashListAdapterFactory> CreateAsync(
            Context context,
            IBuildCacheHttpClientFactory httpClientFactory,
            bool useBlobContentHashLists)
        {
            IBuildCacheHttpClientCommon buildCacheHttpClient = await StartupAsync(httpClientFactory, context, useBlobContentHashLists).ConfigureAwait(false);
            return new ContentHashListAdapterFactory(buildCacheHttpClient);
        }

        /// <summary>
        /// Creates a contenthashlistadapter for a particular session.
        /// </summary>
        public IContentHashListAdapter Create(IBackingContentSession contentSession, bool includeDownloadUris)
        {
            if (BuildCacheHttpClient is ItemBuildCacheHttpClient itemBasedClient)
            {
                return new ItemBuildCacheContentHashListAdapter(itemBasedClient, contentSession.UriCache);
            }

            return new BlobBuildCacheContentHashListAdapter((IBlobBuildCacheHttpClient)BuildCacheHttpClient, contentSession, includeDownloadUris);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            BuildCacheHttpClient.Dispose();
        }

        private static async Task<IBuildCacheHttpClientCommon> StartupAsync(
            IBuildCacheHttpClientFactory httpClientFactory,
            Context context,
            bool useBlobContentHashLists)
        {
            if (useBlobContentHashLists)
            {
                return await httpClientFactory.CreateBlobBuildCacheHttpClientAsync(context);
            }
            else
            {
                return await httpClientFactory.CreateBuildCacheHttpClientAsync(context).ConfigureAwait(false);
            }
        }
    }
}
