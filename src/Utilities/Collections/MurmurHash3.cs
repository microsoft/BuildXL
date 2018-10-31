// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents a 128-bit *non-cryptographic* hash that is well-distributed in all bits.
    /// </summary>
    /// <remarks>
    /// The output is well-distributed in the sense that the hash function used exhibits good 'avalanche';
    /// informally, one can expect that any input change will flip half of the output bits.
    /// Due to this property combined with the large output size, one can treat the <see cref="Low" /> and <see cref="High" />
    /// parts as independent well-distributed hashes (the same argument as in taking a prefix of a SHA1 hash, for example).
    /// Since we can view the high and low parts as two independent hash functions, a single <see cref="MurmurHash3"/>
    /// can derive a sequence of hashes (see 'double hashing'):
    ///     <c>h_i(x) = h_1(x) + i * h_2(x)</c> (where h_1 and h_2 are the high and low components).
    /// See <see cref="GetDerivedHash"/>.
    /// This implementation is based on the public-domain MurmurHash3 (the 128-bit variant).
    /// </remarks>
    public readonly struct MurmurHash3 : IEquatable<MurmurHash3>
    {
        /// <summary>
        /// Low component.
        /// </summary>
        public readonly ulong Low;

        /// <summary>
        /// High component.
        /// </summary>
        public readonly ulong High;

        /// <summary>
        /// Creates a hash wrapper from the given high and low components.
        /// Ensure that the components satisfy the distribution properties of this type.
        /// </summary>
        public MurmurHash3(ulong high, ulong low)
        {
            Low = low;
            High = high;
        }

        /// <nodoc />
        public static MurmurHash3 Zero { get; } = new MurmurHash3(0, 0);

        /// <nodoc />
        public bool IsZero => Low == 0 & High == 0;

        /// <summary>
        /// Calculates the hash at <paramref name="index"/> in the sequence <c>h_i = high + i * low</c>
        /// </summary>
        /// <remarks>
        /// This is a typical construction in 'double hashing' schemes. It also performs well in the context of a Bloom filter,
        /// in which some variable number of distinct hashes are needed per item.
        /// See
        ///        Adam Kirsch and Michael Mitzenmacher. 2008. Less hashing, same performance: Building a better Bloom filter.
        ///        Random Struct. Algorithms 33, 2 (September 2008), 187-218. DOI=10.1002/rsa.v33:2 http://dx.doi.org/10.1002/rsa.v33:2
        /// </remarks>
        public ulong GetDerivedHash(int index)
        {
            return unchecked(High + (Low * (ulong)index));
        }

        /// <summary>
        /// Hashes the given byte array.
        /// </summary>
        public static unsafe MurmurHash3 Create(byte[] key, uint seed)
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
        public static unsafe MurmurHash3 Create(byte* key, uint len, uint seed)
        {
            Contract.Requires(len == 0 || key != null);

            if (len == 0)
            {
                return new MurmurHash3(0, 0);
            }

            unchecked
            {
                byte* data = key;
                uint numBlocks = len / 16;

                ulong h1 = seed;
                ulong h2 = seed;

                const ulong C1 = 0x87c37b91114253d5;
                const ulong C2 = 0x4cf5ad432745937f;

                // We first consume every 16-byte block available (then handle the less-than-block-size remainder below).
                // Note that we do not check that the blocks are 8-byte aligned (not an error on x86 / x64, but maybe slower?)
                var blocks = (ulong*)data;

                for (int i = 0; i < numBlocks; i++)
                {
                    ulong k1 = blocks[(i * 2) + 0];
                    ulong k2 = blocks[(i * 2) + 1];

                    k1 *= C1;
                    k1 = Bits.RotateLeft(k1, 31);
                    k1 *= C2;
                    h1 ^= k1;

                    h1 = Bits.RotateLeft(h1, 27);
                    h1 += h2;
                    h1 = (h1 * 5) + 0x52dce729;

                    k2 *= C2;
                    k2 = Bits.RotateLeft(k2, 33);
                    k2 *= C1;
                    h2 ^= k2;

                    h2 = Bits.RotateLeft(h2, 31);
                    h2 += h1;
                    h2 = (h2 * 5) + 0x38495ab5;
                }

                // Remaining bytes forming less than a 16-byte block
                byte* tail = &data[numBlocks * 16];

                ulong t1 = 0;
                ulong t2 = 0;

                switch (len & 15)
                {
                        // t2 cases
                    case 15:
                        t2 ^= ((ulong)tail[14]) << 48;
                        goto case 14;
                    case 14:
                        t2 ^= ((ulong)tail[13]) << 40;
                        goto case 13;
                    case 13:
                        t2 ^= ((ulong)tail[12]) << 32;
                        goto case 12;
                    case 12:
                        t2 ^= ((ulong)tail[11]) << 24;
                        goto case 11;
                    case 11:
                        t2 ^= ((ulong)tail[10]) << 16;
                        goto case 10;
                    case 10:
                        t2 ^= ((ulong)tail[9]) << 8;
                        goto case 9;
                    case 9:
                        t2 ^= ((ulong)tail[8]) << 0;
                        t2 *= C2;
                        t2 = Bits.RotateLeft(t2, 33);
                        t2 *= C1;
                        h2 ^= t2;
                        goto case 8;

                        // t1 cases
                    case 8:
                        t1 ^= ((ulong)tail[7]) << 56;
                        goto case 7;
                    case 7:
                        t1 ^= ((ulong)tail[6]) << 48;
                        goto case 6;
                    case 6:
                        t1 ^= ((ulong)tail[5]) << 40;
                        goto case 5;
                    case 5:
                        t1 ^= ((ulong)tail[4]) << 32;
                        goto case 4;
                    case 4:
                        t1 ^= ((ulong)tail[3]) << 24;
                        goto case 3;
                    case 3:
                        t1 ^= ((ulong)tail[2]) << 16;
                        goto case 2;
                    case 2:
                        t1 ^= ((ulong)tail[1]) << 8;
                        goto case 1;
                    case 1:
                        t1 ^= ((ulong)tail[0]) << 0;
                        t1 *= C1;
                        t1 = Bits.RotateLeft(t1, 31);
                        t1 *= C2;
                        h1 ^= t1;
                        break;
                    case 0:
                        break;
                }

                // Finalization
                h1 ^= len;
                h2 ^= len;

                h1 += h2;
                h2 += h1;

                const ulong Fmix1 = 0xff51afd7ed558ccd;
                const ulong Fmix2 = 0xc4ceb9fe1a85ec53;

                h1 ^= h1 >> 33;
                h1 *= Fmix1;
                h1 ^= h1 >> 33;
                h1 *= Fmix2;
                h1 ^= h1 >> 33;

                h2 ^= h2 >> 33;
                h2 *= Fmix1;
                h2 ^= h2 >> 33;
                h2 *= Fmix2;
                h2 ^= h2 >> 33;

                h1 += h2;
                h2 += h1;

                return new MurmurHash3(h2, h1);
            }
        }

        /// <summary>
        /// Combines two content hashes by XOR-ing their bytes. This creates a combined hash in an order-independent manner
        /// (combining a sequence of hashes yields a particular hash, regardless of permutation).
        /// </summary>
        public MurmurHash3 CombineOrderIndependent(in MurmurHash3 right)
        {
            var left = this;

            var high = left.High ^ right.High;
            var low = left.Low ^ right.Low;
            return new MurmurHash3(high, low);
        }

        /// <inheritdoc />
        public bool Equals(MurmurHash3 other)
        {
            return other.High == High && other.Low == Low;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // We pick four arbitrary bytes out of the sixteen we have. That's fine given the distributedness properties of this hash.
            return unchecked((int)High);
        }

        /// <summary>
        /// Returns a hex string representation of this hash.
        /// </summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:x16}{1:x16}", High, Low);
        }

        /// <nodoc />
        public static bool operator ==(MurmurHash3 left, MurmurHash3 right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(MurmurHash3 left, MurmurHash3 right)
        {
            return !left.Equals(right);
        }
    }
}
