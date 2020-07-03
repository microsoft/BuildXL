// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
