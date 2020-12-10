// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    /// <nodoc />
    public record DefaultCopySchedulerConfiguration
    {
        /// <nodoc />
        public CopySemaphoreConfiguration OutboundPullConfiguration { get; init; } = new CopySemaphoreConfiguration();

        /// <nodoc />
        public CopySemaphoreConfiguration OutboundPushConfiguration { get; init; } = new CopySemaphoreConfiguration();
    }
}
