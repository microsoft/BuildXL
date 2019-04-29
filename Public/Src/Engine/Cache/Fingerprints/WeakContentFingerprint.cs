// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// A recursively-computed fingerprint for a pip that is based on the content of its inputs.
    /// This is a 'weak' fingerprint which includes statically-known inputs but not dynamically observed inputs.
    /// </summary>
    public readonly struct WeakContentFingerprint : IFingerprint, IEquatable<WeakContentFingerprint>
    {
        /// <nodoc />
        public readonly Fingerprint Hash;

        /// <summary>
        /// SHA-1 of all zeros to use instead of default(WeakContentFingerprint)
        /// </summary>
        public static readonly WeakContentFingerprint Zero = new WeakContentFingerprint(FingerprintUtilities.ZeroFingerprint);

        /// <summary>
        /// Creates a content fingerprint from the given hash.
        /// The hash should have been constructed according to the definition of a content fingerprint.
        /// </summary>
        public WeakContentFingerprint(Fingerprint fingerprint)
        {
            Contract.Requires(fingerprint.Length == FingerprintUtilities.FingerprintLength);
            Hash = fingerprint;
        }

        /// <inheritdoc />
        Fingerprint IFingerprint.Hash => Hash;

        /// <summary>
        /// Tests if the <paramref name="other" /> content fingerprint represents the same hash.
        /// </summary>
        public bool Equals(WeakContentFingerprint other)
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
            return new ContentFingerprint(Hash);
        }

        /// <summary>
        /// Tests if two content fingerprints represents the same hash.
        /// </summary>
        public static bool operator ==(WeakContentFingerprint left, WeakContentFingerprint right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Tests if two content fingerprints represents distinct hashes.
        /// </summary>
        public static bool operator !=(WeakContentFingerprint left, WeakContentFingerprint right)
        {
            return !left.Equals(right);
        }
    }
}
