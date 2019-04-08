// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Pairing of content hash and size of the corresponding content.
    /// </summary>
    public readonly struct ContentHashWithSize : IEquatable<ContentHashWithSize>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHashWithSize"/> struct.
        /// </summary>
        public ContentHashWithSize(ContentHash contentHash, long size)
        {
            Hash = contentHash;
            Size = size;
        }

        /// <summary>
        ///     Gets the content hash member.
        /// </summary>
        public ContentHash Hash { get; }

        /// <summary>
        ///     Gets the content size member.
        /// </summary>
        public long Size { get; }

        /// <inheritdoc />
        public bool Equals(ContentHashWithSize other)
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
        public static bool operator ==(ContentHashWithSize left, ContentHashWithSize right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ContentHashWithSize left, ContentHashWithSize right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[ContentHash={Hash} Size={Size}]";
        }
    }
}
