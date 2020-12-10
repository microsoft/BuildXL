// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    /// <summary>
    /// Base type for all copy requests that can be handled by the copy scheduler.
    /// </summary>
    public abstract record CopyOperationBase(CopyReason Reason, OperationContext Context);

    /// <summary>
    /// Fetch content from another machine
    /// </summary>
    /// <remarks>
    /// Client-side handling of a pull copy
    /// </remarks>
    public sealed record OutboundPullCopy(
        CopyReason Reason,
        OperationContext Context,
        int Attempt,
        Func<OutboundPullArguments, Task<CopyFileResult>> PerformOperationAsync) : CopyOperationBase(Reason, Context)
    {
        public ContentHash ContentHash { get; init; }
    }

    /// <summary>
    /// Push content into another machine
    /// </summary>
    /// <remarks>
    /// Client-side handling of a pull copy. This type is needed because <see cref="OutboundPushCopy{T}"/> is
    /// parametric, so we can't do type pattern matching on it.
    /// </remarks>
    public abstract record OutboundPushCopyBase(
        CopyReason Reason,
        OperationContext Context,
        ProactiveCopyLocationSource LocationSource,
        int Attempt) : CopyOperationBase(Reason, Context)
    {
        /// <summary>
        /// Used internally for scheduling purposes.
        /// </summary>
        public abstract Task<object> PerformOperationInternalAsync(OutboundPushArguments arguments);
    }

    /// <summary>
    /// Push content into another machine
    /// </summary>
    /// <remarks>
    /// Client-side handling of a pull copy
    /// </remarks>
    public sealed record OutboundPushCopy<T>(
        CopyReason Reason,
        OperationContext Context,
        ProactiveCopyLocationSource LocationSource,
        int Attempt,
        Func<OutboundPushArguments, Task<T>> PerformOperationAsync) : OutboundPushCopyBase(Reason, Context, LocationSource, Attempt)
    {
        /// <inheritdoc />
        public override async Task<object> PerformOperationInternalAsync(OutboundPushArguments arguments)
        {
            return (await PerformOperationAsync(arguments))!;
        }
    }
}
