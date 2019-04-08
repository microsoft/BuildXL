// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.VstsInterfaces
{
    /// <summary>
    /// Provides factory method(s) for creating <see cref="IBlobStoreClient"/>
    /// instances for communicating with the BlobStore service.
    /// </summary>
    public interface IBlobStoreClientFactory
    {
        /// <summary>
        /// Asynchronously creates an IBlobStoreHttpClient
        /// </summary>
        Task<IBlobStoreClient> CreateBlobStoreClientAsync();
    }
}
