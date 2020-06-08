// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <nodoc />
    public sealed class BlobBlockHash : IEquatable<BlobBlockHash>
    {
        /// <nodoc />
        public readonly byte[] HashBytes;

        /// <nodoc />
        public BlobBlockHash(byte[] hashValue) => HashBytes = hashValue;

        /// <nodoc />
        public BlobBlockHash(string hex) => HashBytes = HexUtilities.HexToBytes(hex);

        /// <nodoc />
        public string HashString => HexUtilities.BytesToHex(HashBytes);

        /// <summary>
        /// Returns true/false whether the object is equal to the current <see cref="BlobBlockHash"/>
        /// </summary>
        /// <param name="obj">The object to compare against the current instance</param>
        /// <returns>
        /// <c>true</c> if the objects are equal, otherwise <c>false</c>.
        /// </returns>
        public override bool Equals(object? obj)
        {
            var other = obj as BlobBlockHash;
            return other is object && Equals(other);
        }

        /// <summary>
        /// Returns true/false whether the <see cref="BlobBlockHash"/> is equal to the current <see cref="BlobBlockHash"/>
        /// </summary>
        /// <param name="other">The <see cref="BlobBlockHash"/> to compare against the current instance</param>
        /// <returns>
        /// <c>true</c> if the objects are equal, otherwise <c>false</c>.
        /// </returns>
        public bool Equals(BlobBlockHash? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return (other is object) && HashBytes.SequenceEqual(other.HashBytes);
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
        public override string ToString() => HashString;

        /// <nodoc />
        public static bool operator ==(BlobBlockHash? left, BlobBlockHash? right)
        {
            if (left is null)
            {
                return right is null;
            }

            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(BlobBlockHash? left, BlobBlockHash? right)
        {
            return !(left == right);
        }

    }
}
