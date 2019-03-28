// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.MemoizationStore.Vsts.Adapters
{
    /// <summary>
    /// A factory class for creating contenthashlistadapters
    /// </summary>
    public sealed class ContentHashListAdapterFactory : IDisposable
    {
        private readonly IBuildCacheHttpClientFactory _httpClientFactory;

        private ContentHashListAdapterFactory(IBuildCacheHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Gets a BuildCacheHttpClient
        /// </summary>
        public IBuildCacheHttpClientCommon BuildCacheHttpClient { get; private set; }

        /// <summary>
        /// Creates an instance of the ContentHashListAdapterFactory class.
        /// </summary>
        public static async Task<ContentHashListAdapterFactory> CreateAsync(
            Context context,
            IBuildCacheHttpClientFactory httpClientFactory,
            bool useBlobContentHashLists)
        {
            var adapterFactory = new ContentHashListAdapterFactory(httpClientFactory);
            try
            {
                await adapterFactory.StartupAsync(context, useBlobContentHashLists);
            }
            catch (Exception)
            {
                adapterFactory.Dispose();
                throw;
            }

            return adapterFactory;
        }

        /// <summary>
        /// Creates a contenthashlistadapter for a particular session.
        /// </summary>
        public IContentHashListAdapter Create(IContentSession contentSession)
        {
            ItemBuildCacheHttpClient itemBasedClient = BuildCacheHttpClient as ItemBuildCacheHttpClient;

            if (itemBasedClient != null)
            {
                return new ItemBuildCacheContentHashListAdapter(itemBasedClient);
            }

            return new BlobBuildCacheContentHashListAdapter((IBlobBuildCacheHttpClient)BuildCacheHttpClient, contentSession);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            BuildCacheHttpClient?.Dispose();
        }

        private async Task StartupAsync(Context context, bool useBlobContentHashLists)
        {
            if (useBlobContentHashLists)
            {
                BuildCacheHttpClient = await _httpClientFactory.CreateBlobBuildCacheHttpClientAsync(context).ConfigureAwait(false);
            }
            else
            {
                BuildCacheHttpClient =
                    await _httpClientFactory.CreateBuildCacheHttpClientAsync(context).ConfigureAwait(false);
            }
        }
    }
}
