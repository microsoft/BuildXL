// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Hashing
{
    public sealed class BlobBlockHash : IEquatable<BlobBlockHash>
    {
        public readonly byte[] HashBytes;

        public BlobBlockHash(byte[] hashValue)
        {
            HashBytes = hashValue;
        }

        public BlobBlockHash(string hex)
        {
            HashBytes = HexUtilities.HexToBytes(hex);
        }

        public string HashString => HexUtilities.BytesToHex(HashBytes);

        public static bool operator ==(BlobBlockHash left, BlobBlockHash right)
        {
            if (ReferenceEquals(left, null))
            {
                return ReferenceEquals(right, null);
            }

            return left.Equals(right);
        }

        public static bool operator !=(BlobBlockHash left, BlobBlockHash right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Returns true/false whether the object is equal to the current <see cref="BlobBlockHash"/>
        /// </summary>
        /// <param name="obj">The object to compare against the current instance</param>
        /// <returns>
        /// <c>true</c> if the objects are equal, otherwise <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            var other = obj as BlobBlockHash;
            return other != null && Equals(other);
        }

        /// <summary>
        /// Returns true/false whether the <see cref="BlobBlockHash"/> is equal to the current <see cref="BlobBlockHash"/>
        /// </summary>
        /// <param name="other">The <see cref="BlobBlockHash"/> to compare against the current instance</param>
        /// <returns>
        /// <c>true</c> if the objects are equal, otherwise <c>false</c>.
        /// </returns>
        public bool Equals(BlobBlockHash other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return (other != null) && HashBytes.SequenceEqual(other.HashBytes);
        }

        /// <summary>
        /// Gets the unique hash for this unique identifier for binary content.
        /// </summary>
        /// <returns>
        /// A hash value for the content identifier
        /// </returns>
        public override int GetHashCode()
        {
            return BitConverter.ToInt32(HashBytes, 0);
        }

        /// <summary>
        /// Returns a user-friendly, non-canonical string representation of the unique identifier for binary content
        /// </summary>
        /// <returns>
        /// A user-friendly, non-canonical string representation of the content identifier
        /// </returns>
        public override string ToString()
        {
            return HashString;
        }
    }
}
