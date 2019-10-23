// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <nodoc />
    internal class ProactiveCopyResult : ResultBase
    {
        /// <nodoc />
        public bool WasProactiveCopyNeeded { get; }

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
        }

        /// <nodoc />
        public ProactiveCopyResult(ResultBase other, string message)
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
