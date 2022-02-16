// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Options to TryStore methods of <see cref="IArtifactContentCache"/>
    /// </summary>
    public readonly record struct StoreArtifactOptions
    {
        /// <summary>
        /// Options with <see cref="IsCacheEntryContent"/> set to true.
        /// </summary>
        public static StoreArtifactOptions CacheEntryContent { get; } = new StoreArtifactOptions() with { IsCacheEntryContent = true };

        /// <summary>
        /// Indicates that content is a part of a cache entry and thus publishing
        /// of content for consumption can happen along with publishing the cache entry.
        /// </summary>
        public bool IsCacheEntryContent { get; init; }
    }
}
