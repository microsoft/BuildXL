// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    ///     Provides factory method(s) for creating <see cref="IBuildCacheHttpClient" />
    ///     instances for communicating with the BuildCache service.
    /// </summary>
    public interface IBuildCacheHttpClientFactory
    {
        /// <summary>
        ///     Asynchronously creates an IBuildCacheHttpClient
        /// </summary>
        Task<IBuildCacheHttpClient> CreateBuildCacheHttpClientAsync(Context context);

        /// <summary>
        /// Asynchronously creates a IBlobBuildCacheHttpClient
        /// </summary>
        Task<IBlobBuildCacheHttpClient> CreateBlobBuildCacheHttpClientAsync(Context context);
    }
}
