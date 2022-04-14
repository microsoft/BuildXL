// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents a slice of a StringTable corresponding to a particular string id.
    /// </summary>
    public readonly struct BinaryStringSegment : ICharSpan<BinaryStringSegment>, IEquatable<BinaryStringSegment>
    {
        /// <summary>
        /// An empty array segment.
        /// </summary>
        public static readonly BinaryStringSegment Empty = default(BinaryStringSegment);

        /// <summary>
        /// Indicates if the underlying array should be interpreted as ascii characters
        /// </summary>
        private readonly bool m_isAscii;

        /// <summary>
        /// The underlying array.
        /// </summary>
        private readonly ArrayView<byte> m_value;

        /// <summary>
        /// Creates a segment from a range of an array.
        /// </summary>
        public BinaryStringSegment(byte[] value, int byteIndex, int byteLength, bool isAscii)
            : this(new ArrayView<byte>(value, byteIndex, byteLength), isAscii)
        {
        }

        /// <summary>
        /// Creates a segment from a range of an array.
        /// </summary>
        public BinaryStringSegment(ArrayView<byte> value, bool isAscii)
        {
            Contract.AssertDebug(isAscii || (value.Length % 2) == 0, "UTF-16 must have even number of bytes");
            m_value = value;
            m_isAscii = isAscii;
        }

        /// <summary>
        /// Returns a sub segment of an existing segment.
        /// </summary>
        public BinaryStringSegment Subsegment(int index, int length)
        {
            return m_isAscii
                ? new BinaryStringSegment(m_value.GetSubView(index, length), m_isAscii)
                : new BinaryStringSegment(m_value.GetSubView(index * 2, length * 2), m_isAscii);
        }

        /// <summary>
        /// Gets a <see cref="ReadOnlySpan{T}"/> representation of the current instance.
        /// </summary>
        public ReadOnlySpan<byte> AsSpan() => m_value.AsSpan();

        /// <inheritdoc />
        public bool Equals(BinaryStringSegment other)
        {
            if (Length != other.Length)
            {
                return false;
            }

            if (m_value == other.m_value)
            {
                if (m_isAscii == other.m_isAscii)
                {
                    // same segment, same encoding
                    return true;
                }
            }

            if (m_isAscii == other.m_isAscii)
            {
                // We can use span-based comparison only when both instances have the same "encoding".
                return AsSpan().SequenceEqual(other.AsSpan());
            }

            for (int i = 0; i < Length; i++)
            {
                if (this[i] != other[i])
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
            if (!OnlyContains8BitChars)
            {
                // If the current instance has wide characters but the 'buffer' - does not, then the instances can't be equal.
                return false;
            }

            var thisAsSpan = AsSpan();
            var bufferSpan = buffer.AsSpan(index, thisAsSpan.Length);
            // SequenceEquals is vectorized in .NET Core.
            return thisAsSpan.SequenceEqual(bufferSpan);
        }
        
        /// <summary>
        /// Compares this segment to a 16-bit characters drawn from a byte array starting at some index
        /// </summary>
        public bool Equals16Bit(byte[] buffer, int index)
        {
            // Using less efficient implementation, because the endianness for wide characters is not span friendly.
            // We can't just reinterpret the byte array into ReadOnlySpan<char> until we reverse the current endianness.
            // I.e. we should the lower byte of char in 0-7 bit position and the higher bit in 8-15.
            // But right now the order is reversed.
            for (int i = 0; i < Length; i++)
            {
                var storedCh = (char)((buffer[index++] << 8) | buffer[index++]);
                if (storedCh != this[i])
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
                // Using more complicated logic to make the implementation faster.
                if (OnlyContains8BitChars)
                {
                    var thisAsSpan = AsSpan();
                    for (int i = 0; i < thisAsSpan.Length; i++)
                    {
                        hash = ((hash << 5) + hash) ^ thisAsSpan[i];
                    }
                }
                else
                {
                    // This is still a slow-ish non-span-based path.
                    for (int i = 0; i < Length; i++)
                    {
                        hash = ((hash << 5) + hash) ^ this[i];
                    }
                }

                return (int)hash;
            }
        }

        /// <summary>
        /// Returns a character from the segment.
        /// </summary>
        /// <remarks>
        /// This method is not very efficient (especially compared to just a normal array indexer), so try to avoid using it on a hot path and
        /// use the result of <see cref="AsSpan"/> method instead.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2233:OperationsShouldNotOverflow")]
        public char this[int index]
        {
            get
            {
                if (m_isAscii)
                {
                    return (char)m_value[index];
                }
                else
                {
                    index *= 2;
                    return (char)(m_value[index] << 8 | m_value[index + 1]);
                }
            }
        }

        /// <nodoc />
        public static bool operator ==(BinaryStringSegment left, BinaryStringSegment right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(BinaryStringSegment left, BinaryStringSegment right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            using (var stringBuilderWrapper = Pools.GetStringBuilder())
            {
                var stringBuilder = stringBuilderWrapper.Instance;
                for (int i = 0; i < Length; i++)
                {
                    stringBuilder.Append(this[i]);
                }

                return stringBuilder.ToString();
            }
        }

        /// <summary>
        /// Copy the content of this segment to a byte buffer, assuming the segment only contains ASCII characters.
        /// </summary>
        public void CopyAs8Bit(byte[] buffer, int index)
        {
            if (m_isAscii)
            {
                m_value.CopyTo(0, destinationArray: buffer, destinationIndex: index, length: m_value.Length);
            }
            else
            {
                for (int i = 0; i < Length; i++)
                {
                    char ch = this[i];
                    buffer[index++] = unchecked((byte)ch);
                }
            }
        }

        /// <summary>
        /// Copy the content of this segment to a byte buffer, assuming the segment only contains ASCII characters.
        /// </summary>
        public void CopyAs16Bit(byte[] buffer, int index)
        {
            if (m_isAscii)
            {
                for (int i = 0; i < Length; i++)
                {
                    char ch = this[i];
                    buffer[index++] = (byte)((ch >> 8) & 0xff);
                    buffer[index++] = (byte)((ch >> 0) & 0xff);
                }
            }
            else
            {
                m_value.CopyTo(0, destinationArray: buffer, destinationIndex: index, length: m_value.Length);
            }
        }

        /// <summary>
        /// Checks if the segment only contains valid characters for a path atom.
        /// </summary>
        public bool CheckIfOnlyContainsValidPathAtomChars(out int characterWithError)
        {
            for (int i = 0; i < Length; i++)
            {
                if (!PathAtom.IsValidPathAtomChar(this[i]))
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
            for (int i = 0; i < Length; i++)
            {
                if (!SymbolAtom.IsValidIdentifierAtomChar(this[i]))
                {
                    characterWithError = i;
                    return false;
                }
            }

            characterWithError = -1;
            return true;
        }

        /// <summary>
        /// Gets the underlying byte segment
        /// </summary>
        public ArrayView<byte> UnderlyingBytes => m_value;

        /// <summary>
        /// The length of the segment
        /// </summary>
        public int Length => m_isAscii ? m_value.Length : m_value.Length / 2;

        /// <summary>
        /// Indicates whether this segment only contains ASCII characters.
        /// </summary>
        /// <remarks>
        /// Note that this considers 0 as non-ASCII for the sake of the StringTable which treats character 0
        /// as a special marker.
        /// </remarks>
        public bool OnlyContains8BitChars => m_isAscii;
    }
}
