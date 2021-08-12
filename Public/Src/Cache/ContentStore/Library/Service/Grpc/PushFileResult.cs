// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Represents a result of pushing a file.
    /// </summary>
    public sealed class PushFileResult : BoolResult, ICopyResult
    {
        /// <nodoc />
        public CopyResultCode Status { get; }

        /// <inheritdoc />
        private PushFileResult(CopyResultCode status)
        {
            Status = status;
        }

        /// <nodoc />
        public static PushFileResult SkipContentUnavailable()
            => CreateUnsuccessful(CopyResultCode.FileNotFoundError);

        /// <nodoc />
        public static PushFileResult ServerUnavailable()
            => CreateUnsuccessful(CopyResultCode.ServerUnavailable);

        /// <nodoc />
        public static PushFileResult PushSucceeded(long? size)
            => new PushFileResult(CopyResultCode.Success)
            {
                Size = size
            };

        /// <nodoc />
        public static PushFileResult TimedOut(string? message = null)
            => message == null ? new PushFileResult(CopyResultCode.CopyTimeoutError) : new PushFileResult(CopyResultCode.CopyTimeoutError, message);

        /// <nodoc />
        public static PushFileResult RpcError(Exception e)
            => e.Message.Contains("StatusCode=\"DeadlineExceeded\"") ? PushFileResult.TimedOut("Deadline exceeded") : new PushFileResult(CopyResultCode.RpcError, e);

        /// <nodoc />
        internal static PushFileResult Rejected(RejectionReason rejectionReason)
            => CreateUnsuccessful(rejectionReason switch
            {
                RejectionReason.ContentAvailableLocally => CopyResultCode.Rejected_ContentAvailableLocally,
                RejectionReason.CopyLimitReached => CopyResultCode.Rejected_CopyLimitReached,
                RejectionReason.NotSupported => CopyResultCode.Rejected_NotSupported,
                RejectionReason.OlderThanLastEvictedContent => CopyResultCode.Rejected_OlderThanLastEvictedContent,
                RejectionReason.OngoingCopy => CopyResultCode.Rejected_OngoingCopy,
                _ => CopyResultCode.Rejected_Unknown
            });

        /// <nodoc />
        public static PushFileResult Disabled()
            => CreateUnsuccessful(CopyResultCode.Disabled);

        /// <nodoc />
        public static PushFileResult BandwidthTimeout(string diagnostics)
            => CreateUnsuccessful(CopyResultCode.CopyBandwidthTimeoutError, diagnostics);

        private static PushFileResult CreateUnsuccessful(CopyResultCode status, string? diagnostics = null)
        {
            Contract.Requires(status != CopyResultCode.Success);

            return new PushFileResult(status, status.ToString(), diagnostics);
        }

        /// <inheritdoc />
        private PushFileResult(CopyResultCode status, string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(status != CopyResultCode.Success);

            Status = status;
        }

        /// <inheritdoc />
        public PushFileResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Status = CopyResultCode.Unknown;
        }

        /// <inheritdoc />
        public PushFileResult(Exception exception, string? message = null)
            : base(exception, message)
        {
            Status = CopyResultCode.Unknown;
        }

        /// <inheritdoc />
        public PushFileResult(ResultBase other, string? message = null)
            : base(other, message)
        {
            Status = CopyResultCode.Unknown;
        }

        /// <nodoc />
        public PushFileResult(CopyResultCode status, Exception exception, string? message = null)
            : base(exception, message)
        {
            Contract.Requires(status != CopyResultCode.Success);

            Status = status;
        }

        /// <inheritdoc />
        public override Error? Error
        {
            get
            {
                return Status.IsSuccess() ? null : (base.Error ?? Error.FromErrorMessage(Status.ToString()));
            }
        }

        /// <inheritdoc />
        public double? MinimumSpeedInMbPerSec { get; set; }

        /// <inheritdoc />
        public long? Size { get; private set; }

        /// <nodoc />
        public TimeSpan? HeaderResponseTime { get; set; }

        /// <inheritdoc />
        protected override string GetSuccessString() => Status.ToString();

        /// <nodoc />
        public string GetStatusOrDiagnostics()
            => Diagnostics ?? Status.ToString();

        /// <nodoc />
        public string GetStatusOrErrorMessage()
            => ErrorMessage ?? Status.ToString();
    }

    /// <nodoc />
    public static class PushFileStatusExtensions
    {
        /// <nodoc />
        public static bool IsSuccess(this CopyResultCode status)
            => status == CopyResultCode.Success ||
            status == CopyResultCode.Rejected_OngoingCopy ||
            status == CopyResultCode.Rejected_ContentAvailableLocally;

        /// <nodoc />
        public static bool IsRejection(this CopyResultCode status)
            => status == CopyResultCode.Rejected_ContentAvailableLocally ||
            status == CopyResultCode.Rejected_CopyLimitReached ||
            status == CopyResultCode.Rejected_NotSupported ||
            status == CopyResultCode.Rejected_OlderThanLastEvictedContent ||
            status == CopyResultCode.Rejected_OngoingCopy ||
            status == CopyResultCode.Rejected_Unknown;

        /// <nodoc />
        public static bool QualifiesForRetry(this CopyResultCode status)
            => !status.IsSuccess() && (status.IsRejection() || status == CopyResultCode.ServerUnavailable || status == CopyResultCode.CopyTimeoutError || status == CopyResultCode.CopyBandwidthTimeoutError);
    }
}
