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
    public sealed class PushFileResult : ResultBase
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
        public static PushFileResult PushSucceeded()
            => new PushFileResult(CopyResultCode.Success);

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

        private static PushFileResult CreateUnsuccessful(CopyResultCode status)
        {
            Contract.Requires(status != CopyResultCode.Success);

            return new PushFileResult(status, status.ToString());
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
        public override bool Succeeded => Status.IsSuccess();

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
            => !status.IsSuccess() && (status.IsRejection() || status == CopyResultCode.ServerUnavailable);
    }
}
