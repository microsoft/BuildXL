// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Cache.BuildCacheResource.Model
{
    /// <summary>
    /// An in-memory representation of the JSON containing the cache resource configuration associated to a given 1ESHP
    /// </summary>
    public record HostedPoolBuildCacheConfiguration
    {
        /// <summary>
        /// The collection of 1ES build caches associated to the given pool
        /// </summary>
        public required IReadOnlyCollection<BuildCacheConfiguration> AssociatedBuildCaches { get; init; }
    }
}
