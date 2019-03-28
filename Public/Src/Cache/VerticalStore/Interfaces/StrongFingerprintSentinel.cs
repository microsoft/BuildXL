// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Storage;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

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
