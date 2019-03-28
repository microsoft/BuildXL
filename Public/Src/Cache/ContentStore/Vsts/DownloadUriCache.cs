// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using BlobIdentifier = BuildXL.Cache.ContentStore.Hashing.BlobIdentifier;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// A cache for download URIs.
    /// </summary>
    internal class DownloadUriCache
    {
        /// <summary>
        /// A singleton cache per process of download URIs.
        /// </summary>
        internal static readonly DownloadUriCache Instance = new DownloadUriCache();

        private readonly ConcurrentDictionary<ContentHash, PreauthenticatedUri> _downloadUriCacheDictionary
            = new ConcurrentDictionary<ContentHash, PreauthenticatedUri>();

        private DownloadUriCache()
        {
        }

        /// <summary>
        /// Tries to get the download URI from the cache if available.
        /// </summary>
        /// <param name="hash">The contenthash for the blob being requested.</param>
        /// <param name="uri">The download URI if found.</param>
        /// <returns>Whether or not a download URI was found.</returns>
        public bool TryGetDownloadUri(ContentHash hash, out PreauthenticatedUri uri)
        {
            return _downloadUriCacheDictionary.TryGetValue(hash, out uri);
        }

        /// <summary>
        /// Adds a preauthenticated DownloadUri to the cache.
        /// </summary>
        /// <param name="hash">The contenthash for which the DownloadUri provides the content.</param>
        /// <param name="uri">The preauthenticated URI to add to the cache.</param>
        public void AddDownloadUri(ContentHash hash, PreauthenticatedUri uri)
        {
            _downloadUriCacheDictionary.AddOrUpdate(hash, uri, (oldHash, oldUri) => uri);
        }

        /// <summary>
        /// Bulk adds download URIs to the cache.
        /// </summary>
        public void BulkAddDownloadUris(IDictionary<string, Uri> blobDownloadUris)
        {
            if (blobDownloadUris == null)
            {
                return;
            }

            foreach (var blobDownloadUri in blobDownloadUris)
            {
                AddDownloadUri(
                    BlobIdentifier.Deserialize(blobDownloadUri.Key).ToContentHash(),
                    new PreauthenticatedUri(blobDownloadUri.Value, EdgeType.Unknown)); // EdgeType value shouldn't matter because we don't use it.
            }
        }
    }
}
