// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;

#nullable enable
namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Represents a result of Proactive Push (i.e. even when no GrpcCopyClient.Push ever happened)
    /// <see cref="PushFileResult" /> belongs to GrpcCopyClient and hence isn't capable to handle scenarios when no GrpcCopyClient.Push ever happened
    /// This result will have either one of the following properties set to null : <see cref="ProactiveCopyStatus" />, <see cref="PushFileResult" />
    /// When GrpcCopyClient.Push event took place, <see cref="PushFileResult" /> will contain that result
    /// Having an additional status could be misleading when <see cref="PushFileResult" /> fails and an independent <see cref="ProactivePushStatus" /> is passed
    /// If GrpcCopyClient.Push event didn't take place <see cref="ProactiveCopyStatus" /> will contain status representing the reason
    /// Both of these properties will never have values together
    /// </summary>
    public sealed class ProactivePushResult : ResultBase
    {
        /// <summary>
        /// Number of attempts made to produce this result
        /// </summary>
        public int Attempt { get; }

        /// <summary>
        /// Status of Proactive copy when <see cref="PushFileResult" /> is Not Applicable
        /// </summary>
        public ProactivePushStatus? ProactiveCopyStatus { get; }

        /// <summary>
        /// GrpcCopyClient.Push result when that event took place
        /// </summary>
        public PushFileResult? PushFileResult { get; }
        
        /// <nodoc />
        public ProactivePushResult(ResultBase other, string? message = null)
            : base(other, message)
        {
        }

        private ProactivePushResult(PushFileResult result, int attempt)
        {
            Contract.Assert(ProactiveCopyStatus != ProactivePushStatus.Success);
            Attempt = attempt;
            PushFileResult = result;
        }

        private ProactivePushResult(ProactivePushStatus proactiveCopyStatus, int attempt)
        {
            Contract.Assert(PushFileResult == null);
            Attempt = attempt;
            ProactiveCopyStatus = proactiveCopyStatus;
        }

        /// <summary>
        /// <see cref="ProactivePushResult" /> when <see cref="PushFileResult" /> is available
        /// </summary>
        public static ProactivePushResult FromPushFileResult(PushFileResult result, int retry)
        {
            return new ProactivePushResult(result, retry);
        }

        /// <summary>
        /// <see cref="ProactivePushResult" /> when <see cref="PushFileResult" /> is not available and the reason is known
        /// </summary>
        public static ProactivePushResult FromStatus(ProactivePushStatus proactiveCopyStatus, int retry)
        {
            return new ProactivePushResult(proactiveCopyStatus, retry);
        }

        /// <summary>
        /// Status of Proactive Push
        /// Gets Proactivepush Status when <see cref="PushFileResult" /> isn't available
        /// </summary>
        public string? Status => PushFileResult != null ? PushFileResult.Status.ToString() : ProactiveCopyStatus?.ToString();

        /// <summary>
        /// Whether the result needs to be retired
        /// prioritize <see cref="PushFileResult" /> retry eligibility when available
        /// </summary>
        public bool QualifiesForRetry => (PushFileResult != null) ? PushFileResult.Status.QualifiesForRetry() : !Succeeded;

        /// <inheritdoc />
        public override bool Succeeded => (PushFileResult != null) ? PushFileResult.Status.IsSuccess() : ProactiveCopyStatus == ProactivePushStatus.Skipped || ProactiveCopyStatus == ProactivePushStatus.MachineAlreadyHasCopy;

        public bool Rejected => (PushFileResult != null) ? PushFileResult.Status.IsRejection() : !Succeeded;

        public bool Skipped => (PushFileResult != null) ? PushFileResult.Status == CopyResultCode.Disabled : ProactiveCopyStatus == ProactivePushStatus.Skipped;
    }
}
