// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// A recursively-computed fingerprint for a pip that is based on the content of its inputs.
    /// </summary>
    /// <remarks>
    /// This fingerprint can be computed for a node in a graph only when its inputs are available.
    /// This is a struct wrapping only a ContentHash. The sizes should be identical, and we forward all behavior.
    /// The goal here is to establish a new type identity for the same value representation, since fingerprint types
    /// are not interchangeable (despite being same-sized hash digests). Unfortunately, there is no C# equivalent of
    /// Haskell's 'newtype' and so we must write some boilerplate.
    /// </remarks>
    public readonly struct ContentFingerprint : IFingerprint, IEquatable<ContentFingerprint>
    {
        /// <nodoc />
        public readonly Fingerprint Hash;

        /// <summary>
        /// SHA-1 of all zeros to use instead of default(ContentFingerprint)
        /// </summary>
        public static readonly ContentFingerprint Zero = new ContentFingerprint(FingerprintUtilities.ZeroFingerprint);

        /// <summary>
        /// Creates a content fingerprint from the given hash.
        /// The hash should have been constructed according to the definition of a content fingerprint.
        /// </summary>
        public ContentFingerprint(Fingerprint fingerprint)
        {
            Hash = fingerprint;
        }

        /// <nodoc />
        public ContentFingerprint(BinaryReader reader)
        {
            Hash = FingerprintUtilities.CreateFrom(reader);
        }

        /// <nodoc />
        public void WriteTo(BinaryWriter writer)
        {
            Hash.WriteTo(writer);
        }

        /// <inheritdoc />
        Fingerprint IFingerprint.Hash => Hash;

        /// <summary>
        /// Tests if the <paramref name="other" /> content fingerprint represents the same hash.
        /// </summary>
        public bool Equals(ContentFingerprint other)
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
        /// Tests if two content fingerprints represents the same hash.
        /// </summary>
        public static bool operator ==(ContentFingerprint left, ContentFingerprint right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Tests if two content fingerprints represents distinct hashes.
        /// </summary>
        public static bool operator !=(ContentFingerprint left, ContentFingerprint right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Tests if this fingerprint is the default value.
        /// </summary>
        public bool IsDefault => Hash.Length == 0;
    }
}
