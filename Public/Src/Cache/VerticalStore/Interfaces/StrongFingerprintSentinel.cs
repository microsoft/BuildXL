// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Tracing;
using BuildXL.Storage.Fingerprints;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// StrongFingerprint subclass used to indicate that the cost of an enumeration is about to increase
    /// </summary>
    [EventData]
    public sealed class StrongFingerprintSentinel : StrongFingerprint
    {
        /// <nodoc/>
        private StrongFingerprintSentinel()
            : base(WeakFingerprintHash.NoHash, CasHash.NoItem, new Hash(FingerprintUtilities.ZeroFingerprint), "Sentinel")
        {
        }

        /// <summary>
        /// The one instance of this class
        /// </summary>
        public static StrongFingerprintSentinel Instance { get; } = new StrongFingerprintSentinel();
    }
}
