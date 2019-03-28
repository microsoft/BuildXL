// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Artifact content cache that can be used for testing.
    /// </summary>
    public interface IArtifactContentCacheForTest : IArtifactContentCache
    {
        /// <summary>
        /// Gets the realization mode last used for storing/materializing the file
        /// </summary>
        FileRealizationMode GetRealizationMode(string path);

        /// <summary>
        /// Starts tracking realization modes, clearing the current tracked modes.
        /// </summary>
        void ReinitializeRealizationModeTracking();

        /// <summary>
        /// Clears the contents of the cache
        /// </summary>
        void Clear();

        /// <summary>
        /// Discards content from particular cache levels, if it is present.
        /// </summary>
        void DiscardContentIfPresent(ContentHash content, CacheSites sitesToDiscardFrom);

        /// <summary>
        /// Indicates which cache sites (if any) contain the specified hash.
        /// </summary>
        CacheSites FindContainingSites(ContentHash hash);
    }
}
