// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

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
