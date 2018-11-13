// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        {
            Contract.Assert(isAscii || (value.Length % 2) == 0, "UTF-16 must have even number of bytes");
            m_value = new ArrayView<byte>(value, byteIndex, byteLength);
            m_isAscii = isAscii;
        }

        /// <summary>
        /// Creates a segment from a range of an array.
        /// </summary>
        public BinaryStringSegment(ArrayView<byte> value, bool isAscii)
        {
            Contract.Assert(isAscii || (value.Length % 2) == 0, "UTF-16 must have even number of bytes");
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
        [ContractVerification(false)]
        public bool Equals8Bit(byte[] buffer, int index)
        {
            for (int i = 0; i < Length; i++)
            {
                var storedCh = (char)buffer[index++];
                if (storedCh != this[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compares this segment to a 16-bit characters drawn from a byte array starting at some index
        /// </summary>
        [ContractVerification(false)]
        public bool Equals16Bit(byte[] buffer, int index)
        {
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
        [ContractVerification(false)]
        public override int GetHashCode()
        {
            uint hash = 5381;

            unchecked
            {
                for (int i = 0; i < Length; i++)
                {
                    hash = ((hash << 5) + hash) ^ this[i];
                }

                return (int)hash;
            }
        }

        /// <summary>
        /// Returns a character from the segment.
        /// </summary>
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
        [ContractVerification(false)]
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
        [ContractVerification(false)]
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
        [ContractVerification(false)]
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
        [ContractVerification(false)]
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
        [Pure]
        public int Length => m_isAscii ? m_value.Length : m_value.Length / 2;

        /// <summary>
        /// Indicates whether this segment only contains ASCII characters.
        /// </summary>
        /// <remarks>
        /// Note that this considers 0 as non-ASCII for the sake of the StringTable which treats character 0
        /// as a special marker.
        /// </remarks>
        [ContractVerification(false)]
        public bool OnlyContains8BitChars => m_isAscii;
    }
}
