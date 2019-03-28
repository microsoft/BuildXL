// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// Factory for creating clients to communicate with Artifact.
    /// </summary>
    public interface IArtifactHttpClientFactory : Interfaces.Stores.IStartup<Interfaces.Results.BoolResult>
    {
        /// <summary>
        /// Creates HTTP client to talk to BlobStore for file-level content.
        /// </summary>
        Task<IBlobStoreHttpClient> CreateBlobStoreHttpClientAsync(Context context);

        /// <summary>
        /// Creates HTTP client to talk to DedupStore for chunked content.
        /// </summary>
        Task<IDedupStoreHttpClient> CreateDedupStoreHttpClientAsync(Context context);
    }
}