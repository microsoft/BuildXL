// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
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
        /// The server is available but can't process the request.
        /// </summary>
        RejectedByServer,

        /// <summary>
        /// Push copy is disabled for a given space (in-ring or outside the ring).
        /// </summary>
        Disabled,

        /// <summary>
        /// Error occurred during a push file.
        /// </summary>
        Error,
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
        public static PushFileResult ServerUnavailable()
            => CreateUnsuccessful(PushFileResultStatus.ServerUnavailable);

        /// <nodoc />
        public static PushFileResult PushSucceeded()
            => new PushFileResult(PushFileResultStatus.Success);

        /// <nodoc />
        public static PushFileResult Rejected()
            => CreateUnsuccessful(PushFileResultStatus.RejectedByServer);

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
        public override bool Succeeded => Status == PushFileResultStatus.Success;

        /// <inheritdoc />
        protected override string GetSuccessString() => Status.ToString();

        /// <nodoc />
        public string GetStatusOrDiagnostics()
            => Diagnostics ?? Status.ToString();

        /// <nodoc />
        public string GetStatusOrErrorMessage()
            => ErrorMessage ?? Status.ToString();
    }
}
