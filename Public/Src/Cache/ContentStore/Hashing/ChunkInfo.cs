// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Description of a chunk
    /// </summary>
    public readonly struct ChunkInfo : IEquatable<ChunkInfo>
    {
        /// <summary>
        /// Where in the stream the chunk starts.
        /// </summary>
        public readonly ulong Offset;

        /// <summary>
        /// Size of this chunk.
        /// </summary>
        public readonly uint Size;

        /// <summary>
        /// Hash of the chunk.
        /// </summary>
        public readonly byte[] Hash;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChunkInfo"/> struct.
        /// </summary>
        public ChunkInfo(ulong offset, uint size, byte[] hash)
        {
            Offset = offset;
            Size = size;
            Hash = hash;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"[Offset:{Offset} Size:{Size} Hash:{Hash.ToHex()}]";
        }

        /// <inheritdoc/>
        public bool Equals(ChunkInfo other)
        {
            return Offset == other.Offset && Size == other.Size && ByteArrayComparer.ArraysEqual(Hash, other.Hash);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (!(obj is ChunkInfo))
            {
                return false;
            }

            return Equals((ChunkInfo)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Offset.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Size;
                hashCode = (hashCode * 397) ^ Hash[0];
                return hashCode;
            }
        }

        /// <nodoc />
        public static bool operator ==(ChunkInfo left, ChunkInfo right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ChunkInfo left, ChunkInfo right)
        {
            return !left.Equals(right);
        }
    }
}
