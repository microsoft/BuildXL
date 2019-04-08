// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Utilities to go from byte to hex strings and back.
    /// </summary>
    /// TODO: Unify with HexUtilities on the cache side
    public static class HexHelper
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
    }
}
