// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;

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
