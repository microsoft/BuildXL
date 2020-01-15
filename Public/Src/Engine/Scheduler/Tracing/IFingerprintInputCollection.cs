// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Storage.Fingerprints;

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
