// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service.Grpc;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// A status code for push file operation.
    /// </summary>
    public enum ProactiveCopyStatus
    {
        /// <summary>
        /// A location was pushed successfully at least to one machine.
        /// </summary>
        Success,

        /// <summary>
        /// The whole proactive copy operation was skipped, for instance, because there is enough locations for a given hash.
        /// </summary>
        Skipped,

        /// <summary>
        /// At least one target machine rejected the content and the other machine either rejected the content or the operation failed.
        /// </summary>
        Rejected,

        /// <summary>
        /// Proactive copy failed.
        /// </summary>
        Error,
    }

    /// <summary>
    /// Represents a result of a proactive copy.
    /// </summary>
    /// <remarks>
    /// The operation is considered unsuccessful only when one of the operations (inside the ring or outside the ring)
    /// ended up with an error.
    /// </remarks>
    public class ProactiveCopyResult : ResultBase
    {
        /// <nodoc />
        public ProactiveCopyStatus Status { get; }

        /// <inheritdoc />
        public override bool Succeeded => Status == ProactiveCopyStatus.Success || Status == ProactiveCopyStatus.Skipped;

        /// <nodoc />
        public PushFileResult? RingCopyResult { get; }

        /// <nodoc />
        public PushFileResult? OutsideRingCopyResult { get; }

        /// <nodoc />
        public ContentLocationEntry? Entry { get; }

        /// <nodoc />
        public int Retries { get; }

        /// <nodoc />
        public static ProactiveCopyResult CopyNotRequiredResult { get; } = new ProactiveCopyResult();

        private ProactiveCopyResult()
        {
            Status = ProactiveCopyStatus.Skipped;
        }

        /// <nodoc />
        public ProactiveCopyResult(PushFileResult ringCopyResult, PushFileResult outsideRingCopyResult, int retries, ContentLocationEntry? entry = null)
            : base(GetErrorMessage(ringCopyResult, outsideRingCopyResult), GetDiagnostics(ringCopyResult, outsideRingCopyResult))
        {
            RingCopyResult = ringCopyResult;
            OutsideRingCopyResult = outsideRingCopyResult;
            Retries = retries;
            Entry = entry ?? ContentLocationEntry.Missing;

            var results = new[] {ringCopyResult, outsideRingCopyResult};

            if (results.Any(r => r.Succeeded))
            {
                Status = ProactiveCopyStatus.Success;
            }
            else if (results.Any(r => r.Status.IsRejection()))
            {
                Status = ProactiveCopyStatus.Rejected;
            }
            else if (results.All(r => r.Status == CopyResultCode.Disabled))
            {
                Status = ProactiveCopyStatus.Skipped;
            }
            else
            {
                Status = ProactiveCopyStatus.Error;
            }
        }

        /// <nodoc />
        public ProactiveCopyResult(ResultBase other, string? message = null)
            : base(other, message)
        {
            Status = ProactiveCopyStatus.Error;
        }

        private static string? GetErrorMessage(PushFileResult ringCopyResult, PushFileResult outsideRingCopyResult)
        {
            if (!ringCopyResult.Status.IsSuccess() || !outsideRingCopyResult.Status.IsSuccess())
            {
                return
                    $"Success count: {(ringCopyResult.Succeeded ^ outsideRingCopyResult.Succeeded ? 1 : 0)} " +
                    $"RingMachineResult=[{ringCopyResult.GetStatusOrErrorMessage()}] " +
                    $"OutsideRingMachineResult=[{outsideRingCopyResult.GetStatusOrErrorMessage()}] ";
            }

            return null;
        }

        private static string? GetDiagnostics(PushFileResult ringCopyResult, PushFileResult outsideRingCopyResult)
        {
            if (!ringCopyResult.Status.IsSuccess() || !outsideRingCopyResult.Status.IsSuccess())
            {
                return
                    $"RingMachineResult=[{ringCopyResult.GetStatusOrDiagnostics()}] " +
                    $"OutsideRingMachineResult=[{outsideRingCopyResult.GetStatusOrDiagnostics()}] ";
            }

            return null;
        }

        /// <inheritdoc />
        protected override string GetSuccessString()
        {
            if (Status == ProactiveCopyStatus.Skipped)
            {
                return "Success: No copy needed";
            }

            Contract.AssertNotNull(RingCopyResult);
            Contract.AssertNotNull(OutsideRingCopyResult);

            // This must be the case when ring copy and outside ring copy succeeded or were gracefully rejected.
            return $"[Ring={RingCopyResult.Status}, OutsideRing={OutsideRingCopyResult.Status}]";
        }

        /// <inheritdoc />
        protected override string GetErrorString()
        {
            if (Status == ProactiveCopyStatus.Error)
            {
                return base.GetErrorString();
            }

            Contract.AssertNotNull(RingCopyResult);
            Contract.AssertNotNull(OutsideRingCopyResult);

            return $"[{RingCopyResult.Status}, {OutsideRingCopyResult.Status}]";
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{base.ToString()} Entry=[{Entry}]";
        }
    }
}
