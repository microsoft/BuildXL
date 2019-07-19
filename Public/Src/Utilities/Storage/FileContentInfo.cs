// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Storage
{
    /// <summary>
    /// <see cref="FileContentInfo"/> is a file's discovered content hash and size.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)] // This attribute decreases the struct size 32 to 28 bytes (remove 4 bytes of padding needed to align Length)
    public readonly struct FileContentInfo : IEquatable<FileContentInfo>
    {
        /// <summary>
        /// m_lengthAndExistence represents length for actual files, files with unknown length (LengthAndExistence.IsKnownLength == false),
        /// or <see cref="PathExistence"/> if this data does not refer to a file
        /// </summary>
        private readonly LengthAndExistence m_lengthAndExistence;

        /// <summary>
        /// Content hash, contingent upon the <see cref="Native.IO.Usn"/> matching.
        /// </summary>
        public readonly ContentHash Hash;

        /// <summary>
        /// Encapsulates file length and existence. Neither of the values is required.
        /// </summary>
        public readonly struct LengthAndExistence
        {
            // The top 8 bits are used for storing PathExistence value, the rest 56 bits are used to store a file length.
            // The top in each of these block is used to indicate whether the value is specified.            
            // 
            // m_data = [IsKnownExistenceFlag | Existence | IsKnownLengthFlag | Length]; 
            //
            // Note: because the left most bit in m_data might be set, don't right shift its value without casting it to ulong or unsetting the bit first.

            private const int NumberExistenceBits = 8;

            private const int ExistenceShift = 64 - NumberExistenceBits;

            private const long LengthBitsMask = (1L << ExistenceShift) - 1;
            private const long IsKnownLengthFlag = 1L << (ExistenceShift - 1);
            private const long LengthValueMask = (1L << (ExistenceShift - 1)) - 1;

            private const long ExistenceBitsMask = ~LengthBitsMask;
            private const long IsKnownExistenceFlag = 1L << 63;
            private const long ExistenceValueMask = ExistenceBitsMask ^ IsKnownExistenceFlag;

            private readonly long m_data;

            /// <summary>
            /// Max file length that can be stored.
            /// </summary>
            public const long MaxSupportedLength = LengthValueMask;

            /// <summary>
            /// Whether the length value has been set.
            /// </summary>
            public bool IsKnownLength => (m_data & IsKnownLengthFlag) != 0;

            /// <summary>
            /// Length value. Returns 0 if <see cref="IsKnownLength" /> is false.
            /// </summary>
            public long Length => m_data & LengthValueMask;

            /// <summary>
            /// The underlying value used for storage.
            /// </summary>
            public long SerializedValue => m_data;

            /// <summary>
            /// The stored existence value. If no value is stored, returns null.
            /// </summary>
            public PathExistence? Existence
            {
                get
                {
                    if ((m_data & IsKnownExistenceFlag) != 0)
                    {
                        return (PathExistence)((m_data & ExistenceValueMask) >> ExistenceShift);
                    }

                    return null;
                }
            }

            /// <summary>
            /// Creates a <see cref="LengthAndExistence"/> with a known length and optional existence value.
            /// </summary>
            public LengthAndExistence(long length, PathExistence? existence)
            {
                if ((length & LengthValueMask) != length)
                {
                    Contract.Assert(false, $"Invalid length value (reserved bits should not be used): {length} ({System.Text.RegularExpressions.Regex.Replace(Convert.ToString(length, 2).PadLeft(64, '0'), ".{4}", "$0 ")})");
                }

                m_data = (existence.HasValue ? (IsKnownExistenceFlag | ((long)existence << ExistenceShift)) : 0L)
                    | IsKnownLengthFlag
                    | length;
            }

            /// <summary>
            /// Creates a <see cref="LengthAndExistence"/> with an unknown length and optional existence value.
            /// </summary>            
            public LengthAndExistence(PathExistence? existence)
            {
                m_data = existence.HasValue ? (IsKnownExistenceFlag | ((long)existence << ExistenceShift)) : 0L;
            }


            private LengthAndExistence(long combinedValue)
            {
                m_data = combinedValue;
            }

            /// <summary>
            /// Creates a <see cref="LengthAndExistence"/> using a combined existence/length value.
            /// </summary>                        
            public static LengthAndExistence Deserialize(long combinedValue)
            {
                // check that if a flag is not set, the corresponding value is not set as well
                if ((combinedValue & IsKnownExistenceFlag) == 0)
                {
                    Contract.Assert((combinedValue & ExistenceValueMask) == 0);
                }

                if ((combinedValue & IsKnownLengthFlag) == 0)
                {
                    Contract.Assert((combinedValue & LengthValueMask) == 0);
                }

                return new LengthAndExistence(combinedValue);
            }
        }

        /// <summary>
        /// Creates a <see cref="FileContentInfo"/> with real USN version information.
        /// </summary>
        public FileContentInfo(ContentHash hash, long length)
        {
            Contract.Requires(length >= 0);

            m_lengthAndExistence = new LengthAndExistence(
                length,
                // if the length is valid, assign the existence value
                IsValidLength(length, hash)
                    ? PathExistence.ExistsAsFile
                    : (PathExistence?)null);

            Hash = hash;
        }

        /// <summary>
        /// Creates a <see cref="FileContentInfo"/> using a combined existence/length value. 
        /// </summary>
        /// <remarks>
        /// Mainly used for deserialization and files with unknown length.
        /// </remarks>
        public FileContentInfo(ContentHash hash, LengthAndExistence lengthAndExistence)
        {
            m_lengthAndExistence = lengthAndExistence;
            Hash = hash;
        }

        /// <summary>
        /// Creates a <see cref="FileContentInfo"/> with a hash but no known length.
        /// This is intended for abstract hashes that don't correspond to real files on disk.
        /// </summary>
        public static FileContentInfo CreateWithUnknownLength(ContentHash hash, PathExistence? existence = null)
        {
            return new FileContentInfo(hash, new LengthAndExistence(existence));
        }

        /// <summary>
        /// Check if the length of the file is known
        /// </summary>
        public bool HasKnownLength => m_lengthAndExistence.IsKnownLength && IsValidLength(m_lengthAndExistence.Length, Hash);

        /// <summary>
        /// Checks if the hash type of the file matches the specified type.
        /// </summary>
        public bool MatchesHashType(HashType hashType)
            => hashType == HashType.Unknown /* unknown matches everything */ || Hash.HashType == hashType;

        /// <summary>
        /// The file length. If <see cref="HasKnownLength"/> is false, this returns zero.
        /// </summary>
        public long Length => IsValidLength(m_lengthAndExistence.Length, Hash) ? m_lengthAndExistence.Length : 0;

        /// <summary>
        /// The underlying value for file length/path existence. This property (and not Length property) must be used for serialization.
        /// </summary>
        public long SerializedLengthAndExistence => m_lengthAndExistence.SerializedValue;

        /// <summary>
        /// Gets the optional path existence file
        /// </summary>
        public PathExistence? Existence => m_lengthAndExistence.Existence;

        /// <inheritdoc />
        public override string ToString()
        {
            string content = Hash.HashType != HashType.Unknown ? Hash.ToString() : "<UnknownHashType>";
            return I($"[Content {content} (Length: {m_lengthAndExistence})]");
        }

        /// <inheritdoc />
        public bool Equals(FileContentInfo other)
        {
            return other.Hash == Hash && other.SerializedLengthAndExistence == SerializedLengthAndExistence;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Hash.GetHashCode(), SerializedLengthAndExistence.GetHashCode());
        }

        private const string RenderSeparator = "_";

        /// <summary>
        /// Renders this object to a string which when parsed with <see cref="Parse(string)"/>
        /// returns a <see cref="FileContentInfo"/> object that is equal to this object.
        ///
        /// The format of the rendered value is $"{Hash}_{Length}".
        /// </summary>
        public string Render()
        {
            Func<string, object, string> propertyRenderer = (name, value) =>
            {
                var valueStr = value.ToString();
                Contract.AssertDebug(
                    !valueStr.Contains(RenderSeparator),
                    I($"Rendered value of the '{name}' property ('{valueStr}') must not contain the designated separator string ('{RenderSeparator}')"));
                return valueStr;
            };
            string hashStr = propertyRenderer(nameof(Hash), Hash);
            string lengthStr = propertyRenderer(nameof(SerializedLengthAndExistence), SerializedLengthAndExistence);
            return I($"{hashStr}{RenderSeparator}{lengthStr}");
        }

        /// <summary>
        /// Parses <paramref name="format"/> into a <see cref="FileContentInfo"/> object.
        ///
        /// The value  must correspond to the format defined by the <see cref="Render"/> method
        /// or <see cref="ContractException"/> is thrown.
        /// </summary>
        public static FileContentInfo Parse(string format)
        {
            Contract.Requires(format != null);

            string[] splits = format.Split(new[] { RenderSeparator }, StringSplitOptions.None);
            if (splits.Length != 2)
            {
                throw Contract.AssertFailure(I($"Invalid format: expected '{RenderSeparator}' to divide '{format}' into exactly 2 parts."));
            }

            ContentHash hash;
            if (!ContentHash.TryParse(splits[0], out hash))
            {
                throw Contract.AssertFailure(I($"Invalid ContentHash format: '{splits[0]}'"));
            }

            long lengthAndExistence;
            if (!long.TryParse(splits[1], out lengthAndExistence))
            {
                throw Contract.AssertFailure(I($"Invalid file length format: '{splits[1]}'"));
            }

            return new FileContentInfo(hash, LengthAndExistence.Deserialize(lengthAndExistence));
        }

        /// <summary>
        /// Return if the length is valid.
        ///    - Negative length is invalid
        ///    - Zero length is valid only for the "empty file" hash
        ///    - A special hash turns any length into an invalid length
        /// </summary>
        [Pure]
        public static bool IsValidLength(long length, ContentHash hash)
        {
            if (length < 0
                || length > LengthAndExistence.MaxSupportedLength
                || hash.IsSpecialValue())
            {
                return false;
            }

            ContentHash emptyHash = HashInfoLookup.Find(hash.HashType).EmptyHash;
            return length == 0 ? (hash == emptyHash) : (hash != emptyHash);
        }

        /// <nodoc />
        public static bool operator ==(FileContentInfo left, FileContentInfo right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FileContentInfo left, FileContentInfo right)
        {
            return !left.Equals(right);
        }
    }
}
