// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Text;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents a slice of a StringBuilder.
    /// </summary>
    public readonly struct StringBuilderSegment : ICharSpan<StringBuilderSegment>, IEquatable<StringBuilderSegment>
    {
        /// <summary>
        /// An empty segment.
        /// </summary>
        public static readonly StringBuilderSegment Empty = default(StringBuilderSegment);

        /// <summary>
        /// The underlying StringBuilder.
        /// </summary>
        private readonly StringBuilder m_value;

        /// <summary>
        /// The start of the segment.
        /// </summary>
        private readonly int m_index;

        /// <summary>
        /// Creates a segment from a range of a StringBuilder.
        /// </summary>
        public StringBuilderSegment(StringBuilder value, int index, int length)
        {
            Contract.Requires(value != null);
            Contract.Requires(Range.IsValid(index, length, value.Length));

            m_value = value;
            m_index = index;
            Length = length;
        }

        /// <summary>
        /// Creates a segment from a StringBuilder.
        /// </summary>
        public StringBuilderSegment(StringBuilder value)
        {
            Contract.Requires(value != null);

            m_value = value;
            m_index = 0;
            Length = value.Length;
        }

        /// <summary>
        /// Returns a sub segment of an existing segment.
        /// </summary>
        public StringBuilderSegment Subsegment(int index, int length)
        {
            Contract.Requires(Range.IsValid(index, length, Length));

            return new StringBuilderSegment(m_value, m_index + index, length);
        }

        /// <inheritdoc />
        public bool Equals(StringBuilderSegment other)
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

            int thisIndex = m_index;
            int otherIndex = other.m_index;
            for (int i = 0; i < Length; i++)
            {
                if (m_value[thisIndex + i] != other.m_value[otherIndex + i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compares this segment to a 8-bit character array starting at some index
        /// </summary>
        public bool Equals8Bit(byte[] buffer, int index)
        {
            Contract.Requires(buffer != null);
            Contract.Requires(Range.IsValid(index, Length, buffer.Length));

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
            Contract.Requires(buffer != null);
            Contract.Requires(Range.IsValid(index, Length, buffer.Length));

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
        [ContractVerification(false)]
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
        public char this[int index]
        {
            get
            {
                Contract.Requires(index >= 0 && index < Length);
                return m_value[m_index + index];
            }
        }

        /// <nodoc />
        public static bool operator ==(StringBuilderSegment left, StringBuilderSegment right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(StringBuilderSegment left, StringBuilderSegment right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return m_value.ToString(m_index, Length);
        }

        /// <summary>
        /// Copy the content of this segment to a byte buffer, assuming the segment only contains ASCII characters.
        /// </summary>
        public void CopyAs8Bit(byte[] buffer, int index)
        {
            Contract.Requires(buffer != null);
            Contract.Requires(Range.IsValid(index, Length, buffer.Length));

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
            Contract.Requires(buffer != null);
            Contract.Requires(Range.IsValid(index, Length, buffer.Length));

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
        [ContractVerification(false)]
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
        [ContractVerification(false)]
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
        [Pure]
        public int Length { get; }

        /// <summary>
        /// Indicates whether this segment only contains ASCII characters.
        /// </summary>
        /// <remarks>
        /// Note that this considers 0 as non-ASCII for the sake of the StringTable which treats character 0
        /// as a special marker.
        /// </remarks>
        [ContractVerification(false)]
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
}
