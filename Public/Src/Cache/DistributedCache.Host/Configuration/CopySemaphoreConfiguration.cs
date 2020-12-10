// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    /// <nodoc />
    public record CopySemaphoreConfiguration
    {
        /// <nodoc />
        public int MaximumConcurrency { get; init; } = 512;

        /// <nodoc />
        public SemaphoreOrder SemaphoreOrder { get; init; } = SemaphoreOrder.NonDeterministic;

        /// <nodoc />
        public TimeSpan? WaitTimeout { get; init; }
    }
}
