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
        /// Use -1 to specify that the file size in not known.
        /// </summary>
        public const long UnknownLength = -1;

        /// <summary>
        /// m_length represents length for actual files, Unknown, or <see cref="PathExistence"/> if this data does not refer to a file
        /// When representing existence (m_length = UnknownLength - ((int)Existence)) Consequently, Existence = ((PathExistence)UnknownLength - m_length).
        /// </summary>
        private readonly long m_length;

        /// <summary>
        /// Content hash, contingent upon the <see cref="Native.IO.Usn"/> matching.
        /// </summary>
        public readonly ContentHash Hash;

        /// <summary>
        /// Creates a <see cref="FileContentInfo"/> with real USN version information.
        /// </summary>
        public FileContentInfo(ContentHash hash, long length)
        {
            m_length = length;
            Hash = hash;
        }

        /// <summary>
        /// Creates a <see cref="FileContentInfo"/> with a hash but no known length.
        /// This is intended for abstract hashes that don't correspond to real files on disk.
        /// </summary>
        public static FileContentInfo CreateWithUnknownLength(ContentHash hash, PathExistence? existence = null)
        {
            var length = existence == null ? UnknownLength : (UnknownLength - (int)existence);
            return new FileContentInfo(hash, length);
        }

        /// <summary>
        /// Check if the length of the file is known
        /// </summary>
        public bool HasKnownLength => IsValidLength(m_length, Hash);

        /// <summary>
        /// The file length. If <see cref="HasKnownLength"/> is false, this returns zero.
        /// </summary>
        public long Length => m_length > 0 ? m_length : 0;

        /// <summary>
        /// Raw value of file length. Can be negative if length is unknown or if this struct does not refer to a file.
        /// If the file length is known, both RawLength and Length properties return the same value.
        /// </summary>
        /// <remarks>
        /// This property (and not Length property) must be used for serialization.
        /// </remarks>
        public long RawLength => m_length;

        /// <summary>
        /// Gets the optional path existence file
        /// </summary>
        public PathExistence? Existence
        {
            get
            {
                if (HasKnownLength)
                {
                    return PathExistence.ExistsAsFile;
                }

                if (m_length == UnknownLength)
                {
                    return null;
                }

                return (PathExistence) (UnknownLength - m_length);
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string content = Hash.HashType != HashType.Unknown ? Hash.ToString() : "<UnknownHashType>";
            return I($"[Content {content} (Length: {m_length})]");
        }

        /// <inheritdoc />
        public bool Equals(FileContentInfo other)
        {
            return other.Hash == Hash && other.RawLength == RawLength;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Hash.GetHashCode(), RawLength.GetHashCode());
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
            string lengthStr = propertyRenderer(nameof(RawLength), m_length);
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

            long length;
            if (!long.TryParse(splits[1], out length))
            {
                throw Contract.AssertFailure(I($"Invalid file length format: '{splits[1]}'"));
            }

            return new FileContentInfo(hash, length);
        }

        /// <summary>
        /// Return if the length is valid.
        ///    - Negative length is invalid
        ///    - Zero length is valid only for the "empty file" hash
        /// </summary>
        [Pure]
        public static bool IsValidLength(long length, ContentHash hash)
        {
            if (length < 0)
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
