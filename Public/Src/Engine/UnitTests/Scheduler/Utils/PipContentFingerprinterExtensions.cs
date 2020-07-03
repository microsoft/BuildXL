// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Storage.Fingerprints;

namespace Test.BuildXL.Scheduler.Utils
{
    /// <summary>
    /// Helper class that simplifies <see cref="PipContentFingerprinter"/> API for testing purposes.
    /// </summary>
    internal static class PipContentFingerprinterExtensions
    {
        /// <summary>
        /// Compute fingerprint for process pip.
        /// </summary>
        public static ContentFingerprint ComputeFingerprint(this PipContentFingerprinter fingerprinter, Process process)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(process != null);

            return fingerprinter.ComputeWeakFingerprint(process);
        }
    }
}
