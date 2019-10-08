// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Security.Cryptography;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Computes <see cref="MurmurHash3"/> hash for the input data
    /// 128 bit output, 64 bit platform version
    /// </summary>
    public class MurmurHashEngine : HashAlgorithm
    {
        private const int HashLengthInBytes = 16;
        private const int HashLengthInBits = HashLengthInBytes * 8;
        private const int ReadSizeInBytes = 16;

        private static readonly ulong s_c1 = 0x87c37b91114253d5L;
        private static readonly ulong s_c2 = 0x4cf5ad432745937fL;

        private ulong m_length;
        private ulong m_high;
        private ulong m_low;

        /// <inheritdoc/>
        public override int HashSize => (int)HashLengthInBits;

        /// <inheritdoc/>
        public override void Initialize()
        {
            Reset();
        }

        private void Reset()
        {
            // initialize hash values to 0
            m_high = 0;
            m_low = 0;

            // reset our length back to 0
            m_length = 0L;
        }

        /// <inheritdoc/>
        protected override void HashCore(byte[] bb, int start, int length)
        {
            int pos = start;
            ulong remaining = (ulong)length;

            // read 128 bits, 16 bytes, 2 longs in each cycle
            while (remaining >= ReadSizeInBytes)
            {
                ulong k1 = bb.GetUInt64(pos);
                pos += ReadSizeInBytes/2;

                ulong k2 = bb.GetUInt64(pos);
                pos += ReadSizeInBytes/2;

                m_length += ReadSizeInBytes;
                remaining -= ReadSizeInBytes;

                MixBody(k1, k2);
            }

            // if the input MOD 16 != 0
            if (remaining > 0)
            {
                ProcessBytesRemaining(bb, remaining, pos);
            }
        }

        /// <inheritdoc/>
        protected override byte[] HashFinal()
        {
            unchecked
            {
                m_high ^= m_length;
                m_low ^= m_length;

                m_high += m_low;
                m_low += m_high;

                m_high = MixFinal(m_high);
                m_low = MixFinal(m_low);

                m_high += m_low;
                m_low += m_high;
            }

            var buffer = new byte[HashLengthInBytes];
            unsafe
            {
                fixed (byte* b = buffer)
                {
                    *((ulong*)b) = m_high;
                    *((ulong*)b + 1) = m_low;
                }
            }

            return buffer;
        }

        private void ProcessBytesRemaining(byte[] bb, ulong remaining, int pos)
        {
            ulong k1 = 0;
            ulong k2 = 0;
            m_length += remaining;

            // little endian (x86) processing
            switch (remaining)
            {
                case 15:
                    k2 ^= (ulong)bb[pos + 14] << 48; // fall through
                    goto case 14;
                case 14:
                    k2 ^= (ulong)bb[pos + 13] << 40; // fall through
                    goto case 13;
                case 13:
                    k2 ^= (ulong)bb[pos + 12] << 32; // fall through
                    goto case 12;
                case 12:
                    k2 ^= (ulong)bb[pos + 11] << 24; // fall through
                    goto case 11;
                case 11:
                    k2 ^= (ulong)bb[pos + 10] << 16; // fall through
                    goto case 10;
                case 10:
                    k2 ^= (ulong)bb[pos + 9] << 8; // fall through
                    goto case 9;
                case 9:
                    k2 ^= (ulong)bb[pos + 8]; // fall through
                    goto case 8;
                case 8:
                    k1 ^= bb.GetUInt64(pos);
                    break;
                case 7:
                    k1 ^= (ulong)bb[pos + 6] << 48; // fall through
                    goto case 6;
                case 6:
                    k1 ^= (ulong)bb[pos + 5] << 40; // fall through
                    goto case 5;
                case 5:
                    k1 ^= (ulong)bb[pos + 4] << 32; // fall through
                    goto case 4;
                case 4:
                    k1 ^= (ulong)bb[pos + 3] << 24; // fall through
                    goto case 3;
                case 3:
                    k1 ^= (ulong)bb[pos + 2] << 16; // fall through
                    goto case 2;
                case 2:
                    k1 ^= (ulong)bb[pos + 1] << 8; // fall through
                    goto case 1;
                case 1:
                    k1 ^= (ulong)bb[pos]; // fall through
                    break;
                default:
                    throw new Exception("Something went wrong with remaining bytes calculation.");
            }

            m_high ^= MixKey1(k1);
            m_low ^= MixKey2(k2);
        }

        private void MixBody(ulong k1, ulong k2)
        {
            unchecked
            {
                m_high ^= MixKey1(k1);

                m_high = RotateLeft(m_high, 27);
                m_high += m_low;
                m_high = m_high * 5 + 0x52dce729;

                m_low ^= MixKey2(k2);

                m_low = RotateLeft(m_low, 31);
                m_low += m_high;
                m_low = m_low * 5 + 0x38495ab5;
            }
        }

        private static ulong MixKey1(ulong k1)
        {
            unchecked
            {
                k1 = k1 * s_c1;
                k1 = RotateLeft(k1, 31);
                k1 = k1 * s_c2;
                return k1;
            }
        }

        private static ulong MixKey2(ulong k2)
        {
            unchecked
            {
                k2 *= s_c2;
                k2 = RotateLeft(k2, 33);
                k2 *= s_c1;
                return k2;
            }
        }

        private static ulong MixFinal(ulong k)
        {
            unchecked
            {
                // avalanche bits

                k ^= k >> 33;
                k *= 0xff51afd7ed558ccdL;
                k ^= k >> 33;
                k *= 0xc4ceb9fe1a85ec53L;
                k ^= k >> 33;
                return k;
            }
        }

        private static ulong RotateLeft(ulong original, int bits)
        {
            return (original << bits) | (original >> (64 - bits));
        }

        private static ulong RotateRight(ulong original, int bits)
        {
            return (original >> bits) | (original << (64 - bits));
        }
    }

    /// <summary>
    /// Convenient extensions for murmur hash
    /// </summary>
    public static class MurmurHashEngineExtensions
    {
        /// <summary>
        /// Xor the given bytes
        /// </summary>
        public static void CombineOrderIndependent(this byte[] left, byte[] right)
        {
            Contract.Requires(left.Length == right.Length);

            for (var i = 0; i < left.Length; i++)
            {
                left[i] ^= right[i];
            }
        }

        internal static unsafe ulong GetUInt64(this byte[] data, int pos)
        {
            // we only read aligned longs, so a simple casting is enough
            fixed (byte* pbyte = &data[pos])
            {
                return *((ulong*)pbyte);
            }
        }
    }
}
