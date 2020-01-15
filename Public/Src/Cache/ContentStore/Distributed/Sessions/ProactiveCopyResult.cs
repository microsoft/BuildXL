// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <nodoc />
    public class ProactiveCopyResult : ResultBase
    {
        /// <nodoc />
        public bool WasProactiveCopyNeeded { get; }

        /// <nodoc />
        public BoolResult RingCopyResult { get; }

        /// <nodoc />
        public BoolResult OutsideRingCopyResult { get; }

        /// <nodoc />
        public static ProactiveCopyResult CopyNotRequiredResult { get; } = new ProactiveCopyResult();

        private ProactiveCopyResult()
        {
            WasProactiveCopyNeeded = false;
        }

        /// <nodoc />
        public ProactiveCopyResult(BoolResult ringCopyResult, BoolResult outsideRingCopyResult)
            : base(GetErrorMessage(ringCopyResult, outsideRingCopyResult), GetDiagnostics(ringCopyResult, outsideRingCopyResult))
        {
            WasProactiveCopyNeeded = true;
            RingCopyResult = ringCopyResult;
            OutsideRingCopyResult = outsideRingCopyResult;
        }

        /// <nodoc />
        public ProactiveCopyResult(ResultBase other, string message = null)
            : base(other, message)
        {
        }

        private static string GetErrorMessage(BoolResult ringCopyResult, BoolResult outsideRingCopyResult)
        {
            if (!ringCopyResult.Succeeded || !outsideRingCopyResult.Succeeded)
            {
                return
                    $"Success count: {(ringCopyResult.Succeeded ^ outsideRingCopyResult.Succeeded ? 1 : 0)} " +
                    $"RingMachineResult=[{(ringCopyResult.Succeeded ? "Success" : ringCopyResult.ErrorMessage)}] " +
                    $"OutsideRingMachineResult=[{(outsideRingCopyResult.Succeeded ? "Success" : outsideRingCopyResult.ErrorMessage)}] ";
            }

            return null;
        }

        private static string GetDiagnostics(BoolResult ringCopyResult, BoolResult outsideRingCopyResult)
        {
            if (!ringCopyResult.Succeeded || !outsideRingCopyResult.Succeeded)
            {
                return
                    $"RingMachineResult=[{(ringCopyResult.Succeeded ? "Success" : ringCopyResult.Diagnostics)}] " +
                    $"OutsideRingMachineResult=[{(outsideRingCopyResult.Succeeded ? "Success" : outsideRingCopyResult.Diagnostics)}] ";
            }

            return null;
        }

        /// <inheritdoc />
        protected override string GetSuccessString()
        {
            return WasProactiveCopyNeeded ? $"Success" : "Success: No copy needed";
        }
    }
}
