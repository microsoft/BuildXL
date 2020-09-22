// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Configuration for <see cref="ResourcePoolV2{TKey, TObject}"/>
    /// </summary>
    public class ResourcePoolConfiguration
    {
        /// <summary>
        /// Maximum time since last usage before an instance gets garbage collected
        /// </summary>
        public TimeSpan MaximumAge { get; set; } = TimeSpan.FromMinutes(55);

        /// <nodoc />
        /// <remarks>
        /// Used only in <see cref="ResourcePool{TKey, TObject}"/>
        /// </remarks>
        public int MaximumResourceCount { get; set; } = 512;

        /// <nodoc />
        /// <remarks>
        /// Used only in <see cref="ResourcePoolV2{TKey, TObject}"/>
        /// </remarks>
        public TimeSpan GarbageCollectionPeriod { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Whether to allow instances to be invalidated by users in order to force re-creation
        /// </summary>
        /// <remarks>
        /// Used only in <see cref="ResourcePool{TKey, TObject}"/>
        /// </remarks>
        public bool EnableInstanceInvalidation { get; set; } = false;
    }
}
