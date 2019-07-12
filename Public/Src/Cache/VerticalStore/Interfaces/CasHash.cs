// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// A Hash that represents something in the content
    /// addressed store (content based hash)
    /// Should not be constructible other than by the Cache
    /// operations.  (Can not manually build one)
    /// </summary>
    [EventData]
    public readonly struct CasHash : IEquatable<CasHash>
    {
        /// <summary>
        /// The underlying hash
        /// </summary>
        public readonly Hash BaseHash;

        /// <summary>
        /// Length of the hash in bytes
        /// </summary>
        public static readonly int Length = ContentHashingUtilities.HashInfo.ByteLength;

        [EventField]
        private Hash Hash => BaseHash;

        /// <nodoc/>
        public CasHash(ContentHash contentHash)
        {
            BaseHash = new Hash(contentHash);
        }

        /// <nodoc/>
        public CasHash(Hash hash)
        {
            BaseHash = hash;
        }

        /// <nodoc/>
        public CasHash(byte[] digest)
        {
            BaseHash = new Hash(digest, Length);
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
            BaseHash.CopyTo(buffer, offset);
        }

        /// <summary>
        /// Return an array of bytes that is the hash
        /// </summary>
        /// <returns>
        /// Array of bytes that represents the hash
        /// </returns>
        public byte[] ToArray()
        {
            return BaseHash.ToArray();
        }

        /// <summary>
        /// Return equivalent ContentHash
        /// </summary>
        public ContentHash ToContentHash()
        {
            return ContentHashingUtilities.CreateFrom(BaseHash.ToArray());
        }

        /// <summary>
        /// Indicates that there is no such cas item.
        /// </summary>
        public static readonly CasHash NoItem = new CasHash(new Hash(ContentHashingUtilities.ZeroHash));

        /// <summary>
        /// Since it is a struct we can not prevent default&lt;CasHash&gt;
        /// but we can detect it because it would have a default&lt;Hash&gt;
        /// which would be identified as not valid.
        /// </summary>
        [EventIgnore]
        public bool IsValid => BaseHash.IsValid;

        /// <summary>
        /// Parses a hex string that represents a <see cref="CasHash" /> (such as one returned by <see cref="ToString" />).
        /// The hex string must not have an 0x prefix. It must consist of exactly <see cref="Length" /> repetitions of
        /// hex bytes, e.g. A4.
        /// </summary>
        public static CasHash Parse(string value)
        {
            Contract.Requires(value != null);

            CasHash ret;
            bool parsed = TryParse(value, out ret);
            Contract.Assume(parsed);
            return ret;
        }

        /// <summary>
        /// Parses a hex string that represents a <see cref="CasHash" /> (such as one returned by <see cref="ToString" />).
        /// The hex string must not have an 0x prefix. It must consist of exactly <see cref="Length" /> repetitions of
        /// hex bytes, e.g. A4.
        /// </summary>
        public static bool TryParse(string value, out CasHash parsed)
        {
            Contract.Requires(value != null);

            parsed = NoItem;
            Hash raw;
            if (!Hash.TryParse(value, out raw))
            {
                return false;
            }

            parsed = new CasHash(raw);
            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public bool Equals(CasHash other)
        {
            return BaseHash.Equals(other.BaseHash);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return BaseHash.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return BaseHash.ToString();
        }

        /// <nodoc />
        public static bool operator ==(CasHash left, CasHash right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(CasHash left, CasHash right)
        {
            return !left.Equals(right);
        }
    }
}
