// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Simple carrier of some data
    /// </summary>
    public sealed class InputAssertionList
    {
        /// <summary>
        /// The strong fingerprint where this input assertion list came from
        /// </summary>
        public readonly StrongFingerprint StrongFingerprintValue;

        /// <summary>
        /// The simple new-line separated text of the input assertion paths
        /// </summary>
        public readonly string InputAssertionListContents;

        /// <summary>
        /// Construct a InputAssertionList carrier object
        /// </summary>
        /// <param name="strongFingerprint">The strong fingerprint</param>
        /// <param name="inputAssertionListContents">A string with the text of the input file list</param>
        public InputAssertionList(StrongFingerprint strongFingerprint, string inputAssertionListContents)
        {
            StrongFingerprintValue = strongFingerprint;
            InputAssertionListContents = inputAssertionListContents;
        }
    }
}
