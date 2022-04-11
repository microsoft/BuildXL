// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Text;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents a slice of a string.
    /// </summary>
    public readonly struct StringSegment : ICharSpan<StringSegment>, IEquatable<StringSegment>
    {
        /// <summary>
        /// An empty string segment.
        /// </summary>
        public static readonly StringSegment Empty = default;

        /// <summary>
        /// The underlying string.
        /// </summary>
        private readonly string m_value;

        /// <summary>
        /// The start of the segment.
        /// </summary>
        private readonly int m_index;

        /// <summary>
        /// Creates a segment from a range of a string.
        /// </summary>
        public StringSegment(string value, int index, int length)
        {
            Contract.RequiresDebug(value != null);
            Contract.RequiresDebug(Range.IsValid(index, length, value.Length));

            m_value = value;
            m_index = index;
            Length = length;
        }

        /// <summary>
        /// Creates a segment from a string.
        /// </summary>
        public StringSegment(string value)
        {
            Contract.RequiresDebug(value != null);

            m_value = value;
            m_index = 0;
            Length = value.Length;
        }

        /// <summary>
        /// Returns a sub segment of an existing segment.
        /// </summary>
        public StringSegment Subsegment(int index, int length)
        {
            Contract.RequiresDebug(Range.IsValid(index, length, Length));

            return new StringSegment(m_value, m_index + index, length);
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> representation of the current instance.
        /// </summary>
        public ReadOnlySpan<char> AsSpan() => m_value.AsSpan(m_index, Length);

        /// <summary>
        /// Returns the index of the first occurrence of a string within the segment, or -1 if the string doesn't occur in the segment.
        /// </summary>
        public int IndexOf(string value)
        {
            Contract.RequiresNotNullOrEmpty(value);

            if (m_value != null)
            {
                int result = m_value.IndexOf(value, m_index, Length, StringComparison.Ordinal);
                if (result >= 0)
                {
                    return result - m_index;
                }
            }

            return -1;
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Performance", "CA1820")]
        public bool Equals(StringSegment other)
        {
            if (Length != other.Length)
            {
                return false;
            }

            if (m_value == other.m_value)
            {
                if (m_index == other.m_index)
                {
                    // same segment
                    return true;
                }
            }

            // Special handling for distinguishing empty vs null strings.
            if (m_value == null || other.m_value == null)
            {
                return false;
            }

            // Using vectorized comparison by using spans.
            return AsSpan().SequenceEqual(other.AsSpan());
        }

        /// <summary>
        /// Compares this segment to a 8-bit character array starting at some index
        /// </summary>
        public bool Equals8Bit(byte[] buffer, int index)
        {
            Contract.RequiresDebug(buffer != null);
            Contract.RequiresDebug(Range.IsValid(index, Length, buffer.Length));

            int end = m_index + Length;
            for (int i = m_index; i < end; i++)
            {
                var storedCh = (char)buffer[index++];
                if (storedCh != m_value[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compares this segment to a 16-bit characters drawn from a byte array starting at some index
        /// </summary>
        public bool Equals16Bit(byte[] buffer, int index)
        {
            Contract.RequiresDebug(buffer != null);
            Contract.RequiresDebug(Range.IsValid(index, Length, buffer.Length));

            int end = m_index + Length;
            for (int i = m_index; i < end; i++)
            {
                var storedCh = (char)((buffer[index++] << 8) | buffer[index++]);
                if (storedCh != m_value[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            uint hash = 5381;

            unchecked
            {
                var end = m_index + Length;
                for (int i = m_index; i < end; i++)
                {
                    hash = ((hash << 5) + hash) ^ m_value[i];
                }

                return (int)hash;
            }
        }

        /// <summary>
        /// Returns a character from the segment.
        /// </summary>
        public char this[int index] => m_value[m_index + index];

        /// <nodoc />
        public static bool operator ==(StringSegment left, StringSegment right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(StringSegment left, StringSegment right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Implicit convert a string into a string segment.
        /// </summary>
        public static implicit operator StringSegment(string value)
        {
            Contract.RequiresDebug(value != null);

            return new StringSegment(value);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return m_value.Substring(m_index, Length);
        }

        /// <summary>
        /// Copy the string segment characters to the string builder
        /// </summary>
        public void CopyTo(StringBuilder stringBuilder)
        {
            stringBuilder.Append(m_value, m_index, Length);
        }

        /// <summary>
        /// Copy the content of this segment to a byte buffer, assuming the segment only contains ASCII characters.
        /// </summary>
        public void CopyAs8Bit(byte[] buffer, int index)
        {
            Contract.RequiresDebug(buffer != null);
            Contract.RequiresDebug(Range.IsValid(index, Length, buffer.Length));

            int end = m_index + Length;
            for (int i = m_index; i < end; i++)
            {
                char ch = m_value[i];
                buffer[index++] = unchecked((byte)ch);
            }
        }

        /// <summary>
        /// Copy the content of this segment to a byte buffer, assuming the segment only contains ASCII characters.
        /// </summary>
        public void CopyAs16Bit(byte[] buffer, int index)
        {
            Contract.RequiresDebug(buffer != null);
            Contract.RequiresDebug(Range.IsValid(index, Length, buffer.Length));

            int end = m_index + Length;
            for (int i = m_index; i < end; i++)
            {
                char ch = m_value[i];
                buffer[index++] = (byte)((ch >> 8) & 0xff);
                buffer[index++] = (byte)((ch >> 0) & 0xff);
            }
        }

        /// <summary>
        /// Checks if the segment only contains valid characters for a path atom.
        /// </summary>
        public bool CheckIfOnlyContainsValidPathAtomChars(out int characterWithError)
        {
            int end = m_index + Length;
            for (int i = m_index; i < end; i++)
            {
                if (!PathAtom.IsValidPathAtomChar(m_value[i]))
                {
                    characterWithError = i;
                    return false;
                }
            }

            characterWithError = -1;
            return true;
        }

        /// <summary>
        /// Checks if the segment only contains valid characters for an identifier atom.
        /// </summary>
        public bool CheckIfOnlyContainsValidIdentifierAtomChars(out int characterWithError)
        {
            int end = m_index + Length;
            for (int i = m_index; i < end; i++)
            {
                if (!SymbolAtom.IsValidIdentifierAtomChar(m_value[i]))
                {
                    characterWithError = i;
                    return false;
                }
            }

            characterWithError = -1;
            return true;
        }

        /// <summary>
        /// The length of the segment
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Indicates whether this string segment only contains ASCII characters.
        /// </summary>
        /// <remarks>
        /// Note that this considers 0 as non-ASCII for the sake of the StringTable which treats character 0
        /// as a special marker.
        /// </remarks>
        public bool OnlyContains8BitChars
        {
            get
            {
                int end = m_index + Length;
                for (int i = m_index; i < end; i++)
                {
                    char ch = m_value[i];
                    if (ch > (char)255 || ch == (char)0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    /// <summary>
    /// StringSegment extension methods
    /// </summary>
    public static class StringSegmentExtensions
    {
        /// <summary>
        /// Creates a string segment from a range of a string.
        /// </summary>
        public static StringSegment Subsegment(this string value, int index, int count)
        {
            Contract.RequiresDebug(value != null);
            Contract.RequiresDebug(Range.IsValid(index, count, value.Length));

            return new StringSegment(value, index, count);
        }
    }
}
