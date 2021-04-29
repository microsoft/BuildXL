// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    public class CopySchedulerResult<T> : Result<T>
        where T : class
    {
        public SchedulerFailureCode? Reason { get; }

        public ThrottleReason? ThrottleReason { get; private set; }

        public CopySchedulerResult(T result) : base(result, isNullAllowed: false) {}

        /// <inheritdoc />
        public CopySchedulerResult(SchedulerFailureCode reason, string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Reason = reason;
        }

        /// <inheritdoc />
        public CopySchedulerResult(SchedulerFailureCode reason, Exception exception, string? message = null)
            : base(exception, message)
        {
            Reason = reason;
        }

        /// <inheritdoc />
        public CopySchedulerResult(SchedulerFailureCode reason, ResultBase other, string? message = null)
            : base(other, message)
        {
            Reason = reason;
        }

        public static CopySchedulerResult<T> TimeOut(TimeSpan? deadline)
        {
            deadline ??= Timeout.InfiniteTimeSpan;

            return new CopySchedulerResult<T>(
                reason: SchedulerFailureCode.Timeout,
                errorMessage: $"Timed out while waiting `{deadline}` for the scheduler");
        }

        public static CopySchedulerResult<T> Throttle(ThrottleReason reason)
        {
            Contract.Requires(reason != CopyScheduling.ThrottleReason.NotThrottled);
            return new CopySchedulerResult<T>(
                reason: SchedulerFailureCode.Throttled,
                errorMessage: $"Throttled due to `{reason}`")
            {
                ThrottleReason = reason,
            };
        }

        public static CopySchedulerResult<T> Shutdown()
        {
            return new CopySchedulerResult<T>(
                reason: SchedulerFailureCode.Shutdown,
                errorMessage: $"Copy scheduler's shutdown has started");
        }
    }
}
