// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using TypeScript.Net.Extensions;
using TypeScript.Net.Scanning;

namespace TypeScript.Net
{
    /// <summary>
    /// Abstract representation of a file content.
    /// </summary>
    /// <remarks>
    /// The original implementation of the parser/scanner was based on a string manipulation.
    /// This is ok, but requires more memory than the current approach.
    /// This class abstracts away the actual file text representation.
    /// </remarks>
    public abstract class TextSource
    {
        /// <summary>
        /// Returns number of characters in an underlying text source.
        /// </summary>
        public abstract int Length { get; }

        /// <summary>
        /// Unsafe: (potentially) expensive operation that gets a text representation of a source.
        /// </summary>
        public abstract string Text { get; }

        /// <summary>
        /// Returns a <see cref="CharacterCodes"/> instance at a given position.
        /// </summary>
        public abstract CharacterCodes CharCodeAt(int tokenPos);

        /// <summary>
        /// Returns a substring for a current text source.
        /// </summary>
        public abstract string Substring(int start, int length);

        /// <summary>
        /// Implicit conversion from <see cref="string"/> to <see cref="TextSource"/>.
        /// </summary>
        public static implicit operator TextSource(string content)
        {
            return new StringBasedTextSource(content);
        }

        /// <summary>
        /// Explicit factory method to create an instance of a character array.
        /// </summary>
        public static TextSource FromCharArray(char[] data, int length)
        {
            return new CharArrayBasedTextSource(data, length);
        }
    }

    /// <summary>
    /// A set of extension methods for <see cref="TextSource"/> class.
    /// </summary>
    public static class TextSourceExtensions
    {
        /// <summary>
        /// Returns a substring based on the start and end positiions.
        /// </summary>
        public static string SubstringFromTo(this TextSource @this, int start, int end)
        {
            return @this.Substring(start, Math.Min(end - start, @this.Length - start));
        }
    }

    /// <summary>
    /// Text source based on the string content.
    /// </summary>
    internal sealed class StringBasedTextSource : TextSource
    {
        private readonly string m_text;

        /// <nodoc/>
        public StringBasedTextSource(string text)
        {
            m_text = text;
        }

        /// <inheritdoc />
        public override int Length => m_text.Length;

        /// <inheritdoc />
        public override string Text => m_text;

        /// <inheritdoc />
        public override CharacterCodes CharCodeAt(int tokenPos)
        {
            return StringExtensions.CharCodeAt(m_text, tokenPos);
        }

        /// <inheritdoc />
        public override string Substring(int start, int length)
        {
            return m_text.Substring(start, length);
        }
    }

    internal sealed class CharArrayBasedTextSource : TextSource
    {
        private readonly int m_length;
        private readonly char[] m_text;

        /// <nodoc/>
        public CharArrayBasedTextSource(char[] text, int length)
        {
            m_text = text;
            m_length = length;
        }

        /// <inheritdoc/>
        public override string Text => new string(m_text);

        /// <inheritdoc/>
        public override int Length => m_length;

        /// <inheritdoc/>
        public override CharacterCodes CharCodeAt(int tokenPos)
        {
            if (tokenPos == m_length)
            {
                return CharacterCodes.NullCharacter;
            }

            return (CharacterCodes)m_text[tokenPos];
        }

        /// <inheritdoc/>
        public override string Substring(int start, int length)
        {
            var segment = new CharArraySegment(m_text, start, length);
            string result;
            if (!Utilities.Pools.StringCache.TryGetValue(segment, out result))
            {
                result = new string(m_text, start, length);
                Utilities.Pools.StringCache.AddItem(segment, result);
            }

            return result;
        }
    }
}
