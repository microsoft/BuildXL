// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    public class CopySchedulerResult<T> : Result<T>
        where T : class
    {
        public SchedulerFailureCode? Reason { get; }

        public ImmediateRejectionReason? RejectionReason { get; private set; }

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

        public static CopySchedulerResult<T> TimeOut()
        {
            return new CopySchedulerResult<T>(
                reason: SchedulerFailureCode.Timeout,
                errorMessage: "Timed out while waiting for the scheduler");
        }

        public static CopySchedulerResult<T> Reject(ImmediateRejectionReason rejectionReason)
        {
            Contract.Requires(rejectionReason != ImmediateRejectionReason.NotRejected);
            return new CopySchedulerResult<T>(
                reason: SchedulerFailureCode.Rejected,
                errorMessage: "Rejected")
            {
                RejectionReason = rejectionReason,
            };
        }
    }
}
