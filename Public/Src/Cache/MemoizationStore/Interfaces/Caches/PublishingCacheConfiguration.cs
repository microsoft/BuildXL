// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Caches
{
    /// <nodoc />
    public abstract class PublishingCacheConfiguration
    {
        /// <summary>
        /// Whether to make sessions wait for the publishing result or not.
        /// </summary>
        [DataMember]
        public bool PublishAsynchronously { get; set; } = true;
    }
}
