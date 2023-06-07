// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache;

public record ClusterStateRecomputeConfiguration
{
    public TimeSpan ActiveToClosed { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan ClosedToExpired { get; set; } = TimeSpan.FromMinutes(50);

    public TimeSpan ExpiredToUnavailable { get; set; } = TimeSpan.FromDays(1);

    public TimeSpan ActiveToExpired => ActiveToClosed + ClosedToExpired;

    public TimeSpan ActiveToUnavailable => ActiveToExpired + ExpiredToUnavailable;
}
