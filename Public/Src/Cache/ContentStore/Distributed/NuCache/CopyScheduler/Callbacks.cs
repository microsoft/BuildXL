// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    public record CopySchedulingSummary(
        int Priority,
        int PriorityQueueLength,
        TimeSpan QueueWait,
        int OverallQueueLength);

    public record OutboundCallbackArguments(
        OperationContext Context,
        CopySchedulingSummary Summary);

    public record OutboundPullArguments(
        OperationContext Context,
        CopySchedulingSummary Summary) : OutboundCallbackArguments(Context, Summary);

    public record OutboundPushArguments(
        OperationContext Context,
        CopySchedulingSummary Summary) : OutboundCallbackArguments(Context, Summary);
}
