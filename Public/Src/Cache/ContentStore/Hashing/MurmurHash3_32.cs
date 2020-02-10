// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Represents a 32-bit *non-cryptographic* hash that is well-distributed in all bits.
    /// </summary>
    /// <remarks>
    /// The output is well-distributed in the sense that the hash function used exhibits good 'avalanche';
    /// informally, one can expect that any input change will flip half of the output bits.
    /// This implementation is based on the public-domain MurmurHash3 (the 32-bit variant).
    /// </remarks>
    public struct MurmurHash3_32 : IEquatable<MurmurHash3_32>
    {
        /// <summary>
        /// Hash value
        /// </summary>
        public readonly uint Hash;

        /// <summary>
        /// Initializes a new instance of the <see cref="MurmurHash3_32"/> struct.
        /// Creates a hash wrapper from the given high and low components.
        /// Ensure that the components satisfy the distribution properties of this type.
        /// </summary>
        public MurmurHash3_32(uint hash)
        {
            Hash = hash;
        }

        /// <nodoc />
        public static MurmurHash3_32 Zero { get; } = new MurmurHash3_32(0u);

        /// <nodoc />
        public bool IsZero => Hash == 0;

        /// <summary>
        /// Hashes the given byte array.
        /// </summary>
        public static unsafe MurmurHash3_32 Create(byte[] key, uint seed = 0)
        {
            Contract.Requires(key != null);

            fixed (byte* b = key)
            {
                return Create(b, (uint)key.Length, seed);
            }
        }

        /// <summary>
        /// Hashes the given byte array.
        /// </summary>
        public static unsafe MurmurHash3_32 Create(byte* key, uint len, uint seed = 0)
        {
            Contract.Requires(len == 0 || key != null);

            if (len == 0)
            {
                return new MurmurHash3_32(0);
            }

            unchecked
            {
                byte* data = key;
                int numBlocks = (int)len / 4;

                uint h1 = seed;

                const uint C1 = 0xcc9e2d51;
                const uint C2 = 0x1b873593;

                // body

                var blocks = (uint*)(data + numBlocks * 4);

                for(int i = -numBlocks; i > 0; i++)
                {
                    uint k1 = blocks[i];

                    k1 *= C1;
                    k1 = RotateLeft(k1,15);
                    k1 *= C2;
                    
                    h1 ^= k1;
                    h1 = RotateLeft(h1,13); 
                    h1 = h1*5+0xe6546b64;
                }

                //----------
                // tail

                var tail = (uint*)(data + numBlocks * 4);

                uint t1 = 0;

                switch(len & 3)
                {
                    case 3: 
                        t1 ^= tail[2] << 16;
                        goto case 2;
                    case 2: 
                        t1 ^= tail[1] << 8;
                        goto case 1;
                    case 1: 
                        t1 ^= tail[0];
                        t1 *= C1; 
                        t1 = RotateLeft(t1, 15); 
                        t1 *= C2; 
                        h1 ^= t1;
                        break;
                    case 0:
                        break;
                };

                //----------
                // finalization

                h1 ^= len;

                const uint Fmix1 = 0x85ebca6b;
                const uint Fmix2 = 0xc2b2ae35;

                // mixing
                h1 ^= h1 >> 16;
                h1 *= Fmix1;
                h1 ^= h1 >> 13;
                h1 *= Fmix2;
                h1 ^= h1 >> 16;

                return new MurmurHash3_32(h1);
            }
        }

        /// <summary>
        /// Shift-and-rotate (left shift, and the bits that fall off reappear on the right)
        /// </summary>
        private static uint RotateLeft(uint value, int shiftBy)
        {
#if DEBUG
            Contract.Assert(shiftBy >= 0 && shiftBy < 16);
#endif
            checked
            {
                return value << shiftBy | (value >> (32 - shiftBy));
            }
        }

        /// <summary>
        /// Gets the byte representation of the hash
        /// </summary>
        public unsafe byte[] ToByteArray()
        {
            byte[] buffer = new byte[4];
            fixed (byte* b = buffer)
            {
                *((uint*)b) = Hash;
            }

            return buffer;
        }

        /// <inheritdoc />
        public bool Equals(MurmurHash3_32 other)
        {
            return other.Hash == Hash;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return unchecked((int)Hash);
        }

        /// <summary>
        /// Returns a hex string representation of this hash.
        /// </summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:x16}", Hash);
        }

        /// <nodoc />
        public static bool operator ==(MurmurHash3_32 left, MurmurHash3_32 right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(MurmurHash3_32 left, MurmurHash3_32 right)
        {
            return !left.Equals(right);
        }
    }
}
