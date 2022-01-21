// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public record ClusterStateRecomputeConfiguration
    {
        public TimeSpan RecomputeFrequency { get; set; } = TimeSpan.FromMinutes(1);

        public TimeSpan ActiveToClosedInterval { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan ActiveToDeadExpiredInterval { get; set; } = TimeSpan.FromHours(1);

        public TimeSpan ClosedToDeadExpiredInterval { get; set; } = TimeSpan.FromHours(1);
    }
}
