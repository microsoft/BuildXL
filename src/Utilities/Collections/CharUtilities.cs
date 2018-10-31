// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Text;

namespace BuildXL.Utilities
{
    /// <summary>
    /// General utilities for characters
    /// </summary>
    public static class CharUtilities
    {
        private static readonly char[] s_toUpperInvariantCache = CreateToUpperInvariantCache();

        /// <summary>
        /// UTF8 without a byte order marker
        /// </summary>
        public static readonly Encoding Utf8NoBomNoThrow = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

        private static char[] CreateToUpperInvariantCache()
        {
            var a = new char[char.MaxValue + 1];
            for (int c = char.MinValue; c <= char.MaxValue; c++)
            {
                a[c] = char.ToUpperInvariant((char)c);
            }

            return a;
        }

        /// <summary>
        /// <code>code.ToUpperInvariant</code> is surprisingly expensive; this is a cache
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char ToUpperInvariantFast(this char character)
        {
            return s_toUpperInvariantCache[character];
        }
    }
}
