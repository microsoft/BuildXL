// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// A generic hash - just to have a name for it
    /// </summary>
    [EventData]
    [DebuggerDisplay("Hash = {HashValue}")]
    public readonly struct Hash : IEquatable<Hash>
    {
        /// <nodoc />
        public readonly Fingerprint RawHash;

        /// <nodoc/>
        [EventField]
        public string HashValue => RawHash.ToHex();

        /// <nodoc/>
        public Hash(ContentHash hash)
        {
            RawHash = new Fingerprint(hash.ToFixedBytes(), hash.Length);
        }

        /// <nodoc/>
        public Hash(Fingerprint fingerprint)
        {
            RawHash = fingerprint;
        }

        /// <nodoc/>
        public Hash(byte[] digest, int length)
        {
            RawHash = new Fingerprint(digest, length);
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
            RawHash.Serialize(buffer, offset);
        }

        /// <summary>
        /// Return an array of bytes that is the hash
        /// </summary>
        /// <returns>
        /// Array of bytes that represents the hash
        /// </returns>
        public byte[] ToArray()
        {
            return RawHash.ToByteArray();
        }

        /// <summary>
        /// Parses a hex string that represents a <see cref="Hash" /> (such as one returned by <see cref="ToString" />).
        /// The hex string must not have an 0x prefix. It must consist of repetitions of hex bytes, e.g. A4.
        /// </summary>
        public static Hash Parse(string value)
        {
            Contract.Requires(value != null);

            bool parsed = TryParse(value, out var ret);
            Contract.Assume(parsed);
            return ret;
        }

        /// <summary>
        /// Parses a hex string that represents a <see cref="Hash" /> (such as one returned by <see cref="ToString" />).
        /// The hex string must not have an 0x prefix. It must consist of repetitions of hex bytes, e.g. A4.
        /// </summary>
        public static bool TryParse(string value, out Hash parsed)
        {
            Contract.Requires(value != null);

            if (!Fingerprint.TryParse(value, out var raw))
            {
                parsed = default(Hash);
                return false;
            }

            parsed = new Hash(raw);
            return true;
        }

        /// <nodoc/>
        [EventIgnore]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:NotReferencingThis", Justification = "Temporary code path?")]
        public bool IsValid => true;

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public bool Equals(Hash other)
        {
            return RawHash.Equals(other.RawHash);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return RawHash.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return RawHash.ToHex();
        }

        /// <nodoc />
        public static bool operator ==(Hash left, Hash right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(Hash left, Hash right)
        {
            return !left.Equals(right);
        }
    }
}
