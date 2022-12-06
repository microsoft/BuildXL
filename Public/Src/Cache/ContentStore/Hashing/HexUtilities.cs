// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BuildXL.Cache.ContentStore.Interfaces.Utils
{
    /// <summary>
    /// Utilities to go from byte to hex strings and back.
    /// </summary>
    /// <remarks>
    /// The implementation is adopted from HexDecoder.TryDecodeFromUtf16.
    /// </remarks>
    public static class HexUtilities
    {
        private const string NullHex = "(null)";
        internal static readonly char[] NybbleToHex = new char[16] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        /// <summary>Map from an ASCII char to its hex value, e.g. arr['b'] == 11. 0xFF means it's not a hex digit.</summary>
        public static ReadOnlySpan<byte> CharToHexLookup => new byte[]
                                                            {
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 15
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 31
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 47
                                                                0x0,  0x1,  0x2,  0x3,  0x4,  0x5,  0x6,  0x7,  0x8,  0x9,  0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 63
                                                                0xFF, 0xA,  0xB,  0xC,  0xD,  0xE,  0xF,  0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 79
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 95
                                                                0xFF, 0xa,  0xb,  0xc,  0xd,  0xe,  0xf,  0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 111
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 127
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 143
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 159
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 175
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 191
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 207
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 223
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 239
                                                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF  // 255
                                                            };

        /// <summary>
        /// Verifies whether the string is in hexadecimal format.
        /// </summary>
        public static bool IsHexString(string data)
        {
            Contract.Requires(data != null);

            return IsHexString(data.AsSpan());
        }

        /// <summary>
        /// Verifies whether the data is in hexadecimal format.
        /// </summary>
        public static bool IsHexString(ReadOnlySpan<char> data)
        {
            if (data.Length % 2 != 0)
            {
                return false;
            }

            for (int i = 0; i < data.Length / 2; i += 2)
            {
                var c1 = data[i];
                var c2 = data[i + 1];

                if (!IsHexChar(c1) || !IsHexChar(c2))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true if <paramref name="c"/> is a hex character.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHexChar(int c)
        {
            if (IntPtr.Size == 8)
            {
                // This code path, when used, has no branches and doesn't depend on cache hits,
                // so it's faster and does not vary in speed depending on input data distribution.
                // We only use this logic on 64-bit systems, as using 64 bit values would otherwise
                // be much slower than just using the lookup table anyway (no hardware support).
                // The magic constant 18428868213665201664 is a 64 bit value containing 1s at the
                // indices corresponding to all the valid hex characters (ie. "0123456789ABCDEFabcdef")
                // minus 48 (ie. '0'), and backwards (so from the most significant bit and downwards).
                // The offset of 48 for each bit is necessary so that the entire range fits in 64 bits.
                // First, we subtract '0' to the input digit (after casting to uint to account for any
                // negative inputs). Note that even if this subtraction underflows, this happens before
                // the result is zero-extended to ulong, meaning that `i` will always have upper 32 bits
                // equal to 0. We then left shift the constant with this offset, and apply a bitmask that
                // has the highest bit set (the sign bit) if and only if `c` is in the ['0', '0' + 64) range.
                // Then we only need to check whether this final result is less than 0: this will only be
                // the case if both `i` was in fact the index of a set bit in the magic constant, and also
                // `c` was in the allowed range (this ensures that false positive bit shifts are ignored).

                // It is imiportant to use unchecked context to avoid getting OverflowExcetions because bxl runs the code in checked context
                // and the cast to long might cause an overflow in some cases.
                unchecked
                {
                    ulong i = (uint)c - '0';
                    ulong shift = 18428868213665201664UL << (int)i;
                    ulong mask = i - 64;

                    return (long)(shift & mask) < 0 ? true : false;
                }
            }

            return FromChar(c) != 0xFF;
        }

        /// <summary>
        /// Parses hexadecimal strings the form '1234abcd' or '0x9876fedb' into
        /// an array of bytes.
        /// </summary>
        /// <remarks>
        /// This method only parses an even number of hexadecimal digits; any odd-length hex string is parsed
        /// leaving the last character un-parsed.
        /// </remarks>
        public static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return CollectionUtilities.EmptyArray<byte>();
            }

            hex = hex.Trim();

            int cur = 0;
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                cur = 2;
            }

            int len = hex.Length - cur;
            if (!TryToByteArrayCore(hex.AsSpan(start: cur, length: len), out var result))
            {
                throw new ArgumentException($"Invalid hex string {hex}");
            }

            return result;
        }

        /// <summary>
        /// Parses hexadecimal strings of the form '1234abcd' or '0x9876fedb' into
        /// a pre-allocated array of bytes.
        /// </summary>
        /// <param name="hex">The hex string. Assumed to be non-null and to contain valid hex characters.</param>
        /// <param name="buffer">
        /// A caller-allocated buffer that is assumed to be non-null and at least the correct
        /// length, floor(hex.Length / 2), to accept the bytes.
        /// </param>
        /// <returns>
        /// A span on <paramref name="buffer"/> referring to the resulting bytes.
        /// If the length of the buffer is longer than the bytes generated from the
        /// hexadecimal, this span can be shorter than the buffer.
        /// </returns>
        /// <remarks>
        /// This method only parses an even number of hexadecimal digits; any odd-length hex string is parsed
        /// leaving the last character un-parsed.
        /// </remarks>
        public static ReadOnlySpan<byte> HexToBytes(string hex, byte[] buffer)
        {
            if (TryDecodeFromUtf16(hex.AsSpan(), buffer.AsSpan(), out var charsProcessed))
            {
                return buffer.AsSpan(start: 0, length: charsProcessed / 2);
            }

            throw new ArgumentException($"Invalid hex string {hex}");
        }

        /// <summary>
        /// Converts the provided bytes into a hexadecimal string of the form '1234abcd'.
        /// </summary>
        public static string ToHex(this IList<byte>? bytes)
        {
            if (bytes == null)
            {
                return NullHex;
            }

            var chars = new char[bytes.Count * 2];
            for (int i = 0; i < bytes.Count; i++)
            {
                byte b = bytes[i];
                chars[i * 2] = NybbleToHex[(b & 0xF0) >> 4];
                chars[i * 2 + 1] = NybbleToHex[b & 0x0F];
            }
            return new string(chars);
        }

        /// <summary>
        /// Converts the provided bytes into a hexadecimal string of the form '1234abcd'.
        /// </summary>
        public static string BytesToHex(IList<byte> bytes) => ToHex(bytes);

        /// <summary>
        /// Tries to convert a hexadecimal string into an array of bytes, ensuring the hexadecimal string
        /// has valid characters and is of even length.
        /// </summary>
        /// <remarks>
        /// This is the ADO compatible hex to byte array utility.
        /// Compared to <see cref="HexToBytes(string)"/>, this implementation does not perform conversion if
        /// the hexadecimal string has odd length.
        /// </remarks>
        public static bool TryToByteArray(string hexString, [NotNullWhen(true)]out byte[]? bytes)
        {
            Contract.Requires(hexString != null);
            return TryToByteArray(hexString.AsSpan(), out bytes);
        }

        /// <inheritdoc cref="TryToByteArray(string,out byte[])"/>
        public static bool TryToByteArray(ReadOnlySpan<char> chars, [NotNullWhen(true)]out byte[]? bytes)
        {
            if (!IsHexString(chars))
            {
                bytes = null;
                return false;
            }

            return TryToByteArrayCore(chars, out bytes);
        }

        private static bool TryToByteArrayCore(ReadOnlySpan<char> chars, [NotNullWhen(true)]out byte[]? bytes)
        {
#if NET5_0_OR_GREATER
            bytes = GC.AllocateUninitializedArray<byte>(chars.Length >> 1);
#else
            bytes = new byte[chars.Length >> 1];
#endif
            if (!TryDecodeFromUtf16(chars, bytes, out _))
            {
                bytes = null;
                return false;
            }

            return true;
        }

        private static bool TryDecodeFromUtf16(ReadOnlySpan<char> chars, Span<byte> bytes, out int charsProcessed)
        {
            int i = 0;
            int j = 0;
            int byteLo = 0;
            int byteHi = 0;
            while (i < chars.Length - 1)
            {
                byteLo = FromChar(chars[i + 1]);
                byteHi = FromChar(chars[i]);

                // byteHi hasn't been shifted to the high half yet, so the only way the bitwise or produces this pattern
                // is if either byteHi or byteLo was not a hex character.
                if ((byteLo | byteHi) == 0xFF)
                {
                    break;
                }

                bytes[j++] = (byte)((byteHi << 4) | byteLo);
                i += 2;
            }

            if (byteLo == 0xFF)
            {
                i++;
            }

            charsProcessed = i;
            return (byteLo | byteHi) != 0xFF;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FromChar(int c)
        {
            return c >= CharToHexLookup.Length ? 0xFF : CharToHexLookup[c];
        }
    }
}
