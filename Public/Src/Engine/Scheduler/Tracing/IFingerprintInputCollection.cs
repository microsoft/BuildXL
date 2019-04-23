// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine.Cache;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// An interface to capture fingerprint inputs from
    /// <see cref="IExecutionLogEventData{TSelf}"/> objects.
    /// </summary>
    public interface IFingerprintInputCollection
    {
        /// <summary>
        /// Uses an <see cref="IFingerprinter"/> to write fingerprint inputs
        /// as they would have been consumed during fingerprint computation.
        /// </summary>
        void WriteFingerprintInputs(IFingerprinter writer);
    }
}
