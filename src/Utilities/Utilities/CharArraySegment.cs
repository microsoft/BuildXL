// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
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
            Contract.Requires(value != null);
            Contract.Requires(Range.IsValid(index, length, value.Length));

            m_value = value;
            m_index = index;
            m_length = length;
        }

        /// <summary>
        /// Creates a segment from an array.
        /// </summary>
        public CharArraySegment(char[] value)
        {
            Contract.Requires(value != null);

            m_value = value;
            m_index = 0;
            m_length = value.Length;
        }

        /// <summary>
        /// Returns a sub segment of an existing segment.
        /// </summary>
        public CharArraySegment Subsegment(int index, int length)
        {
            Contract.Requires(Range.IsValid(index, length, Length));

            return new CharArraySegment(m_value, m_index + index, length);
        }

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

            int thisIndex = m_index;
            int otherIndex = other.m_index;
            for (int i = 0; i < m_length; i++)
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
            Contract.Requires(buffer != null);
            Contract.Requires(Range.IsValid(index, Length, buffer.Length));

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
        [ContractVerification(false)]
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
        public char this[int index]
        {
            get
            {
                Contract.Requires(index >= 0 && index < Length);
                return m_value[m_index + index];
            }
        }

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
            Contract.Requires(buffer != null);
            Contract.Requires(Range.IsValid(index, Length, buffer.Length));

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
            Contract.Requires(buffer != null);
            Contract.Requires(Range.IsValid(index, Length, buffer.Length));

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
        [ContractVerification(false)]
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
        [ContractVerification(false)]
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
        [Pure]
        public int Length
        {
            get { return m_length; }
        }

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
