// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Pairing of content hash and size of the corresponding content.
    /// </summary>
    public readonly struct ShortHashWithSize : IEquatable<ShortHashWithSize>
    {
        /// <nodoc />
        public ShortHashWithSize(ShortHash contentHash, long size)
        {
            Hash = contentHash;
            Size = size;
        }

        /// <nodoc />
        public ShortHash Hash { get; }

        /// <nodoc />
        public long Size { get; }

        /// <inheritdoc />
        public bool Equals(ShortHashWithSize other)
        {
            return Hash.Equals(other.Hash) && Size == other.Size;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Hash.GetHashCode() ^ Size.GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(ShortHashWithSize left, ShortHashWithSize right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ShortHashWithSize left, ShortHashWithSize right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public static implicit operator ShortHashWithSize(ContentHashWithSize value)
        {
            return new ShortHashWithSize(value.Hash, value.Size);
        }

        /// <nodoc />
        public static implicit operator ShortHashWithSize((ShortHash hash, long size) value)
        {
            return new ShortHashWithSize(value.hash, value.size);
        }

        /// <nodoc />
        public static implicit operator ShortHashWithSize((ContentHash hash, long size) value)
        {
            return new ShortHashWithSize(value.hash, value.size);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={Hash} Size={Size}]";
        }
    }
}
