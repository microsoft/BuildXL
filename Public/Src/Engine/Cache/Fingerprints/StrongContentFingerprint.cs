// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// Namespace tag used in computing <see cref="StrongContentFingerprint"/>s. This allows trivial side-by-side
    /// of various methods of computing a strong fingerprint.
    /// </summary>
    /// <remarks>
    /// To use this, put something like 'StrongContentFingerprintNamespace=N' (for integer N) in your hash stream.
    /// </remarks>
    public enum StrongContentFingerprintNamespace
    {
        /// <summary>
        /// The strong content fingerprint is an extension of a weak fingerprint with no additional data.
        /// </summary>
        /// <remarks>
        /// This is included just for the sake of shims (see <see cref="SinglePhase.SinglePhaseFingerprintStoreAdapter"/>).
        /// </remarks>
        Null = 0,

        /// <summary>
        /// The strong fingerprint is based on (path, content) pairs for observed process inputs.
        /// </summary>
        ObservedInputs = 1,
    }

    /// <summary>
    /// A recursively-computed fingerprint for a pip that is based on the content of its inputs.
    /// This is a 'strong' fingerprint which includes both statically-known inputs as well as dynamically observed inputs.
    /// In other words, this is a <see cref="WeakContentFingerprint"/> augmented with additional inputs.
    /// </summary>
    public readonly struct StrongContentFingerprint : IFingerprint, IEquatable<StrongContentFingerprint>
    {
        /// <nodoc />
        public readonly Fingerprint Hash;

        /// <summary>
        /// SHA-1 of all zeros to use instead of default(StrongContentFingerprint)
        /// </summary>
        public static readonly StrongContentFingerprint Zero = new StrongContentFingerprint(FingerprintUtilities.ZeroFingerprint);

        /// <summary>
        /// Special marker fingerprint for marking selectors (i.e. <see cref="PublishedEntryRef"/>) whose path set is used to augment the weak fingerprint
        /// </summary>
        public static readonly StrongContentFingerprint AugmentedWeakFingerprintMarker = new StrongContentFingerprint(FingerprintUtilities.CreateSpecialValue(1));

        /// <summary>
        /// Creates a content fingerprint from the given hash.
        /// The hash should have been constructed according to the definition of a strong content fingerprint.
        /// </summary>
        public StrongContentFingerprint(Fingerprint hash)
        {
            Contract.Requires(hash.Length == FingerprintUtilities.FingerprintLength);
            Hash = hash;
        }

        /// <inheritdoc />
        Fingerprint IFingerprint.Hash => Hash;

        /// <summary>
        /// Creates a hashing helper for computing a strong fingerprint.
        /// The helper is seeded with the strong fingerprint namespace and related weak fingerprint.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public static HashingHelper CreateHashingHelper(
            PathTable pathTable,
            bool recordFingerprintString)
        {
            var hasher = new HashingHelper(pathTable, recordFingerprintString);
            return hasher;
        }

        /// <summary>
        /// Tests if the <paramref name="other" /> content fingerprint represents the same hash.
        /// </summary>
        public bool Equals(StrongContentFingerprint other)
        {
            return Hash.Equals(other.Hash);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Hash.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Hash.ToHex();
        }

        /// <summary>
        /// Tests if the given object is a content fingerprint that represents the same hash.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Returns a generic <see cref="BuildXL.Engine.Cache.Fingerprints.ContentFingerprint"/> (not distinguished as strong or weak).
        /// TODO: The generic fingerprint type can go away as soon as we *only* do two-phase (weak -> strong) lookups;
        ///       as of writing we are transitioning.
        /// </summary>
        public ContentFingerprint ToGenericFingerprint()
        {
            return new ContentFingerprint(FingerprintUtilities.CreateFrom(Hash.ToByteArray()));
        }

        /// <summary>
        /// Tests if two content fingerprints represents the same hash.
        /// </summary>
        public static bool operator ==(StrongContentFingerprint left, StrongContentFingerprint right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Tests if two content fingerprints represents distinct hashes.
        /// </summary>
        public static bool operator !=(StrongContentFingerprint left, StrongContentFingerprint right)
        {
            return !left.Equals(right);
        }
    }
}
