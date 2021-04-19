// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Utils
{
    /// <summary>
    /// Utilities to go from byte to hex strings and back.
    /// </summary>
    /// TODO: Unify with HexUtilities on the cache side
    public static class HexUtilities
    {
        private const string NullHex = "(null)";
        private static readonly char[] s_nybbleToHex = new char[16] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        // Indexed by Unicode character value after value of '0' (zero character).
        // IndexOutOfRangeException gets thrown if characters out of covered range are used.
        // Gets 0x100 for invalid characters in the range, for single-branch detection of
        // invalid characters.
        private static readonly ushort[] s_hexToNybble =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9,  // Character codes 0-9.
            0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100,  // Character codes ":;<=>?@"
            10, 11, 12, 13, 14, 15,  // Character codes A-F
            0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100,  // G-P
            0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100, 0x100,  // Q-Z
            0x100, 0x100, 0x100, 0x100, 0x100, 0x100,  // Character codes "[\]^_`"
            10, 11, 12, 13, 14, 15,  // Character codes a-f
        };

        /// <summary>
        /// Verifies whether the string is in hexadecimal format.
        /// </summary>
        public static bool IsHexString(string data)
        {
            Contract.Requires(data != null);

            return data.Length % 2 == 0 && data.All(c => (c >= '0' && c <= '9') ||
                                                         (c >= 'a' && c <= 'f') ||
                                                         (c >= 'A' && c <= 'F'));
        }

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
            if (!IsHexString(hexString))
            {
                bytes = null;
                return false;
            }

            // surely there is a better way to get a byte[] from a hex string...
            bytes = new byte[hexString.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
            }

            return true;
        }

        /// <summary>
        /// Parses hexadecimal strings the form '1234abcd' or '0x9876fedb' into
        /// an array of bytes.
        /// </summary>
        /// <remarks>
        /// We use a bit of custom code to avoid thrashing the heap with temp strings when
        /// using Convert.ToByte(hex.Substring(n, 2)). This method only parses an even number of
        /// hexadecimal digits; any odd-length hex string is parsed leaving the last character
        /// un-parsed.
        /// </remarks>
        public static byte[] HexToBytes(string hex)
        {
            if (hex == null)
            {
                return CollectionUtilities.EmptyArray<byte>();
            }

            hex = hex.Trim();

            int cur;
            if (!hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                cur = 0;
            }
            else
            {
                cur = 2;
            }

            int len = hex.Length - cur;
            var result = new byte[len / 2];
            int index = 0;

            const string ExceptionMessage = "Invalid hex string ";
            try
            {
                for (; cur < (hex.Length - 1); cur += 2)
                {
                    int b = (s_hexToNybble[hex[cur] - '0'] << 4) | s_hexToNybble[hex[cur + 1] - '0'];
                    if (b < 256)
                    {
                        result[index++] = (byte)b;
                    }
                    else
                    {
                        throw new ArgumentException(ExceptionMessage + hex, nameof(hex));
                    }
                }
            }
            catch (IndexOutOfRangeException)
            {
                throw new ArgumentException(ExceptionMessage + hex, nameof(hex));
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
        /// We use a bit of custom code to avoid thrashing the heap with temp strings when
        /// using Convert.ToByte(hex.Substring(n, 2)). This method only parses an even number of
        /// hexadecimal digits; any odd-length hex string is parsed leaving the last character
        /// un-parsed.
        /// </remarks>
        public static ReadOnlySpan<byte> HexToBytes(string hex, byte[] buffer)
        {
            int index = 0;

            for (int cur = 0; cur < (hex.Length - 1); cur += 2)
            {
                int b = (s_hexToNybble[hex[cur] - '0'] << 4) | s_hexToNybble[hex[cur + 1] - '0'];
                buffer[index++] = (byte)b;
            }

            return buffer.AsSpan(0, index);
        }

        /// <summary>
        /// Converts the provided bytes into a hexadecimal string of the form '1234abcd'.
        /// </summary>
        public static string ToHex(this IList<byte> bytes)
        {
            if (bytes == null)
            {
                return NullHex;
            }

            var chars = new char[bytes.Count * 2];
            for (int i = 0; i < bytes.Count; i++)
            {
                byte b = bytes[i];
                chars[i * 2] = s_nybbleToHex[(b & 0xF0) >> 4];
                chars[i * 2 + 1] = s_nybbleToHex[b & 0x0F];
            }
            return new string(chars);
        }

        /// <summary>
        /// Converts the provided bytes into a hexadecimal string of the form '1234abcd'.
        /// </summary>
        public static string BytesToHex(IList<byte> bytes) => ToHex(bytes);
    }
}
