// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace TypeScript.Net.Scanning
{
    /// <summary>
    /// Custom text builder class optimized for minimal memory usage.
    /// </summary>
    /// <remarks>
    /// Unlike regular <see cref="System.Text.StringBuilder"/>, the text builder will not copy string or reallocate buffer
    /// if the only one string was used to compute text.
    /// In many cases this exactly the case and this trick allows to reuse strings from the cache instead
    /// of allocating new string each time.
    /// </remarks>
    public sealed class TextBuilder
    {
        private const int DefaultSize = 1024;
        private readonly List<string> m_strings = new List<string>(DefaultSize);

        /// <nodoc />
        public void Append(string str)
        {
            m_strings.Add(str);
        }

        /// <nodoc />
        public override string ToString()
        {
            if (m_strings.Count == 0)
            {
                return string.Empty;
            }

            if (m_strings.Count == 1)
            {
                return m_strings[0];
            }

            return string.Join(string.Empty, m_strings);
        }

        /// <nodoc />
        public void Clear() => m_strings.Clear();

        /// <nodoc />
        public static TextBuilder operator +(TextBuilder left, string right)
        {
            left.Append(right);
            return left;
        }
    }
}
