// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Represents a slice of a char[].
    /// </summary>
    public readonly struct CharArraySegment : ICharSpan<CharArraySegment>, System.IEquatable<CharArraySegment>
    {
        /// <summary>
        /// An empty array segment.
        /// </summary>
        public static readonly CharArraySegment Empty = default(CharArraySegment);

        /// <summary>
        /// The underlying array.
        /// </summary>
        private readonly char[] m_value;

        /// <summary>
        /// The start of the segment.
        /// </summary>
        private readonly int m_index;

        /// <summary>
        /// The length of the segment
        /// </summary>
        private readonly int m_length;

        /// <summary>
        /// Creates a segment from a range of an array.
        /// </summary>
        public CharArraySegment(char[] value, int index, int length)
        {
            Contract.RequiresDebug(value != null);
            Contract.RequiresDebug(Range.IsValid(index, length, value.Length));

            m_value = value;
            m_index = index;
            m_length = length;
        }

        /// <summary>
        /// Creates a segment from an array.
        /// </summary>
        public CharArraySegment(char[] value)
        {
            Contract.RequiresDebug(value != null);

            m_value = value;
            m_index = 0;
            m_length = value.Length;
        }

        /// <summary>
        /// Returns a sub segment of an existing segment.
        /// </summary>
        public CharArraySegment Subsegment(int index, int length)
        {
            Contract.RequiresDebug(Range.IsValid(index, length, Length));

            return new CharArraySegment(m_value, m_index + index, length);
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> representation of the current instance.
        /// </summary>
        public ReadOnlySpan<char> AsSpan() => m_value.AsSpan(m_index, m_length);

        /// <inheritdoc />
        public bool Equals(CharArraySegment other)
        {
            if (m_length != other.m_length)
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

            // Using vectorized comparison via spans.
            return AsSpan().SequenceEqual(other.AsSpan());
        }

        /// <summary>
        /// Compares this segment to a 8-bit character array starting at some index
        /// </summary>
        public bool Equals8Bit(byte[] buffer, int index)
        {
            Contract.RequiresDebug(buffer != null);
            Contract.RequiresDebug(Range.IsValid(index, Length, buffer.Length));

            int end = m_index + m_length;
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

            int end = m_index + m_length;
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
                var end = m_index + m_length;
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
        public static bool operator ==(CharArraySegment left, CharArraySegment right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(CharArraySegment left, CharArraySegment right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return new string(m_value, m_index, Length);
        }

        /// <summary>
        /// Copy the content of this segment to a byte buffer, assuming the segment only contains ASCII characters.
        /// </summary>
        public void CopyAs8Bit(byte[] buffer, int index)
        {
            Contract.RequiresDebug(buffer != null);
            Contract.RequiresDebug(Range.IsValid(index, Length, buffer.Length));

            int end = m_index + m_length;
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

            int end = m_index + m_length;
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
            int end = m_index + m_length;
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
            int end = m_index + m_length;
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
        public int Length => m_length;

        /// <summary>
        /// Indicates whether this segment only contains ASCII characters.
        /// </summary>
        /// <remarks>
        /// Note that this considers 0 as non-ASCII for the sake of the StringTable which treats character 0
        /// as a special marker.
        /// </remarks>
        public bool OnlyContains8BitChars
        {
            get
            {
                int end = m_index + m_length;
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
}
