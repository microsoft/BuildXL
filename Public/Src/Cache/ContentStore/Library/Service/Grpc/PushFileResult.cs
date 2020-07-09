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
    /// A status code for push file operation.
    /// </summary>
    public enum PushFileResultStatus
    {
        /// <summary>
        /// The push operation is successful.
        /// </summary>
        Success,

        /// <summary>
        /// The server is unavailable.
        /// </summary>
        ServerUnavailable,

        /// <summary>
        /// The server already has the content.
        /// </summary>
        Rejected_ContentAvailableLocally,

        /// <summary>
        /// The server is already handling a copy of the content.
        /// </summary>
        Rejected_OngoingCopy,

        /// <summary>
        /// The server is at the limit of concurrent copies.
        /// </summary>
        Rejected_CopyLimitReached,

        /// <summary>
        /// The server does not have any handlers which support the operation.
        /// </summary>
        Rejected_NotSupported,

        /// <summary>
        /// The server is already evicting older content than this
        /// </summary>
        Rejected_OlderThanLastEvictedContent,

        /// <summary>
        /// Rejected for unknown reasons.
        /// </summary>
        Rejected_Unknown,

        /// <summary>
        /// Push copy is disabled for a given space (in-ring or outside the ring).
        /// </summary>
        Disabled,

        /// <summary>
        /// Error occurred during a push file.
        /// </summary>
        Error,

        /// <summary>
        /// Push copy is skipped because the content already disappeared from the source machine.
        /// </summary>
        SkipContentUnavailable,
    }

    /// <summary>
    /// Represents a result of pushing a file.
    /// </summary>
    public sealed class PushFileResult : ResultBase
    {
        /// <nodoc />
        public PushFileResultStatus Status { get; }

        /// <inheritdoc />
        private PushFileResult(PushFileResultStatus status)
        {
            Status = status;
        }

        /// <nodoc />
        public static PushFileResult SkipContentUnavailable()
            => CreateUnsuccessful(PushFileResultStatus.SkipContentUnavailable);

        /// <nodoc />
        public static PushFileResult ServerUnavailable()
            => CreateUnsuccessful(PushFileResultStatus.ServerUnavailable);

        /// <nodoc />
        public static PushFileResult PushSucceeded()
            => new PushFileResult(PushFileResultStatus.Success);

        /// <nodoc />
        internal static PushFileResult Rejected(RejectionReason rejectionReason)
            => CreateUnsuccessful(rejectionReason switch
            {
                RejectionReason.ContentAvailableLocally => PushFileResultStatus.Rejected_ContentAvailableLocally,
                RejectionReason.CopyLimitReached => PushFileResultStatus.Rejected_CopyLimitReached,
                RejectionReason.NotSupported => PushFileResultStatus.Rejected_NotSupported,
                RejectionReason.OlderThanLastEvictedContent => PushFileResultStatus.Rejected_OlderThanLastEvictedContent,
                RejectionReason.OngoingCopy => PushFileResultStatus.Rejected_OngoingCopy,
                _ => PushFileResultStatus.Rejected_Unknown
            });

        /// <nodoc />
        public static PushFileResult Disabled()
            => CreateUnsuccessful(PushFileResultStatus.Disabled);

        private static PushFileResult CreateUnsuccessful(PushFileResultStatus status)
        {
            Contract.Requires(status != PushFileResultStatus.Success);

            return new PushFileResult(status, status.ToString());
        }

        /// <inheritdoc />
        private PushFileResult(PushFileResultStatus status, string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(status != PushFileResultStatus.Success);

            Status = status;
        }

        /// <inheritdoc />
        public PushFileResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Status = PushFileResultStatus.Error;
        }

        /// <inheritdoc />
        public PushFileResult(Exception exception, string? message = null)
            : base(exception, message)
        {
            Status = PushFileResultStatus.Error;
        }

        /// <inheritdoc />
        public PushFileResult(ResultBase other, string? message = null)
            : base(other, message)
        {
            Status = PushFileResultStatus.Error;
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
        public static bool IsSuccess(this PushFileResultStatus status)
            => status == PushFileResultStatus.Success ||
            status == PushFileResultStatus.Rejected_OngoingCopy ||
            status == PushFileResultStatus.Rejected_ContentAvailableLocally;

        /// <nodoc />
        public static bool IsRejection(this PushFileResultStatus status)
            => status == PushFileResultStatus.Rejected_ContentAvailableLocally ||
            status == PushFileResultStatus.Rejected_CopyLimitReached ||
            status == PushFileResultStatus.Rejected_NotSupported ||
            status == PushFileResultStatus.Rejected_OlderThanLastEvictedContent ||
            status == PushFileResultStatus.Rejected_OngoingCopy ||
            status == PushFileResultStatus.Rejected_Unknown;

        /// <nodoc />
        public static bool QualifiesForRetry(this PushFileResultStatus status)
            => !status.IsSuccess() && (status.IsRejection() || status == PushFileResultStatus.ServerUnavailable);
    }
}
