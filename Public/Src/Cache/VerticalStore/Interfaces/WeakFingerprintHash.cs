// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// A WeakFingerprint is a hash of the know from the spec
    /// files.  The core is that it is a hash.
    /// </summary>
    [EventData]
    public readonly struct WeakFingerprintHash : IEquatable<WeakFingerprintHash>
    {
        /// <summary>
        /// Length of the hash in bytes
        /// </summary>
        public static readonly int Length = FingerprintUtilities.FingerprintLength;

        /// <summary>
        /// A value that indicates a hash was not specified.
        /// </summary>
        public static readonly WeakFingerprintHash NoHash =
            new WeakFingerprintHash(new Hash(FingerprintUtilities.ZeroFingerprint));

        /// <summary>
        /// The hash of the fingerprint.
        /// </summary>
        public readonly Hash FingerprintHash;

        [EventField]
        private Hash Hash => FingerprintHash;

        /// <nodoc/>
        public WeakFingerprintHash(Hash hash)
        {
            Contract.Requires(hash.RawHash.Length == FingerprintUtilities.FingerprintLength);
            FingerprintHash = hash;
        }

        /// <nodoc/>
        public WeakFingerprintHash(byte[] digest)
        {
            FingerprintHash = new Hash(digest, Length);
        }

        /// <nodoc/>
        public static WeakFingerprintHash Random()
        {
            return new WeakFingerprintHash(new Hash(Fingerprint.Random(FingerprintUtilities.FingerprintLength)));
        }

        /// <summary>
        /// Copies the content hash to a byte array starting at the given offset.
        /// </summary>
        /// <remarks>
        /// This requires that offset + Length be
        /// at most the length of the array.
        /// </remarks>
        public void CopyTo(byte[] buffer, int offset)
        {
            FingerprintHash.CopyTo(buffer, offset);
        }

        /// <summary>
        /// Return an array of bytes that is the hash
        /// </summary>
        /// <returns>
        /// Array of bytes that represents the hash
        /// </returns>
        public byte[] ToArray()
        {
            return FingerprintHash.ToArray();
        }

        /// <summary>
        /// Since it is a struct we can not prevent default&lt;WeakFingerprint&gt;
        /// but we can detect it because it would have a default&lt;Hash&gt;
        /// which would be identified as not valid.
        /// </summary>
        [EventIgnore]
        public bool IsValid => FingerprintHash.IsValid;

        /// <summary>
        /// Parses a hex string that represents a <see cref="WeakFingerprintHash" /> (such as one returned by <see cref="ToString" />).
        /// The hex string must not have an 0x prefix. It must consist of exactly <see cref="Length" /> repetitions of
        /// hex bytes, e.g. A4.
        /// </summary>
        public static WeakFingerprintHash Parse(string value)
        {
            Contract.Requires(value != null);

            bool parsed = TryParse(value, out var ret);
            Contract.Assume(parsed);
            return ret;
        }

        /// <summary>
        /// Parses a hex string that represents a <see cref="WeakFingerprintHash" /> (such as one returned by <see cref="ToString" />).
        /// The hex string must not have an 0x prefix. It must consist of exactly <see cref="Length" /> repetitions of
        /// hex bytes, e.g. A4.
        /// </summary>
        public static bool TryParse(string value, out WeakFingerprintHash parsed)
        {
            Contract.Requires(value != null);

            parsed = NoHash;
            if (!Hash.TryParse(value, out var raw))
            {
                return false;
            }

            parsed = new WeakFingerprintHash(raw);
            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public bool Equals(WeakFingerprintHash other)
        {
            return FingerprintHash.Equals(other.FingerprintHash);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return FingerprintHash.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return FingerprintHash.ToString();
        }

        /// <nodoc />
        public static bool operator ==(WeakFingerprintHash left, WeakFingerprintHash right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(WeakFingerprintHash left, WeakFingerprintHash right)
        {
            return !left.Equals(right);
        }
    }
}
