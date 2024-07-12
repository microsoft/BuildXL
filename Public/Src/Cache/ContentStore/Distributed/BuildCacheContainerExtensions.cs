// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed.Blob;

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <nodoc/>
    public static class BuildCacheContainerExtensions
    {
        /// <summary>
        /// Maps a <see cref="BuildCacheContainerType"/> as specified in the build cache resource configuration to a <see cref="BlobCacheContainerPurpose"/>
        /// </summary>
        /// <remarks>
        /// The <paramref name="cacheContainerType"/> is expected to be either <see cref="BuildCacheContainerType.Content"/> or <see cref="BuildCacheContainerType.Metadata"/>
        /// </remarks>
        public static BlobCacheContainerPurpose ToContainerPurpose(this BuildCacheContainerType cacheContainerType) =>
            cacheContainerType switch
            {
                BuildCacheContainerType.Metadata => BlobCacheContainerPurpose.Metadata,
                BuildCacheContainerType.Content => BlobCacheContainerPurpose.Content,
                _ => throw new ArgumentException($"Container type {cacheContainerType} can only be Metada or Content.")
            };
    }
}
