// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using TypeScript.Net.Scanning;

namespace TypeScript.Net.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="string"/> that simplifies migration from TypeScript to C#.
    /// </summary>
    public static class StringExtensions
    {
        /// <nodoc/>
        public static string Replace(this string str, string pattern, string replacePattern)
        {
            return Regex.Replace(str, pattern, replacePattern);
        }

        /// <nodoc/>
        public static CharacterCodes CharCodeAt(this string text, int index)
        {
            if (index == text.Length)
            {
                return CharacterCodes.NullCharacter;
            }

            return (CharacterCodes)text[index];
        }

        /// <nodoc/>
        public static string FromCharCode(this CharacterCodes code)
        {
            // TODO: Optimize
            return ((char)code).ToString();
        }

        /// <nodoc/>
        public static string CharAt(this string str, int index)
        {
            return new string(str[index], 1);
        }

        /// <nodoc/>
        public static string Substr(this string str, int start, int length = 0)
        {
            return str.Substring(start, System.Math.Min(length, str.Length - start));
        }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "ToLowerInvariant() is not being called for normalization.")]
        public static string ToLowerCase(this string str)
        {
            return str.ToLowerInvariant();
        }

        /// <nodoc/>
        public static bool Test(this string pattern, string input)
        {
            return Regex.IsMatch(input, pattern);
        }

        /// <nodoc/>
        public static Match Exec(this string pattern, string input)
        {
            return Regex.Match(input, pattern);
        }

        /// <nodoc/>
        public static string Substring(this string str, int start, int end)
        {
            return str.Substring(start, System.Math.Min(end - start, str.Length - start));
        }
    }
}
