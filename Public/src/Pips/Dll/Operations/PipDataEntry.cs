// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Pip data entry
    /// </summary>
    internal readonly struct PipDataEntry : IEquatable<PipDataEntry>
    {
        /// <summary>
        /// This size of this structure in bytes when serialized using <see cref="Write"/>
        /// </summary>
        public const int BinarySize = 5;

        /// <summary>
        /// Gets the corresponding <see cref="PipDataEntryType"/> for the entry.
        /// </summary>
        public PipDataEntryType EntryType { get; }

        private readonly PipDataFragmentEscaping m_escaping;
        private readonly int m_data;

        /// <summary>
        /// Class constructor
        /// </summary>
        public PipDataEntry(PipDataFragmentEscaping escaping, PipDataEntryType entryType, int data)
        {
            Contract.Requires(escaping == PipDataFragmentEscaping.Invalid || entryType == PipDataEntryType.NestedDataHeader);
            EntryType = entryType;
            m_escaping = escaping;
            m_data = data;
        }

        /// <summary>
        /// Gets the corresponding <see cref="PipDataFragmentEscaping" /> for the entry.
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where <see cref="EntryType" /> is equal to <see cref="PipDataEntryType.NestedDataHeader" />.
        /// </remarks>
        public PipDataFragmentEscaping Escaping
        {
            get
            {
                Contract.Requires(EntryType == PipDataEntryType.NestedDataHeader);
                return m_escaping;
            }
        }

        /// <summary>
        /// Gets the corresponding <see cref="PipFragmentType"/> for the entry.
        /// </summary>
        public PipFragmentType FragmentType
        {
            get
            {
                switch (EntryType)
                {
                    case PipDataEntryType.AbsolutePath:
                        return PipFragmentType.AbsolutePath;
                    case PipDataEntryType.StringLiteral:
                        return PipFragmentType.StringLiteral;
                    case PipDataEntryType.NestedDataHeader:
                        return PipFragmentType.NestedFragment;
                    default:
                        return PipFragmentType.Invalid;
                }
            }
        }

        /// <summary>
        /// Returns the current value as <see cref="AbsolutePath" />;
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where <see cref="EntryType" /> is equal to
        /// <see cref="PipDataEntryType.AbsolutePath" />.
        /// </remarks>
        /// <returns>Value as a <see cref="AbsolutePath" /></returns>
        public AbsolutePath GetPathValue()
        {
            Contract.Requires(EntryType == PipDataEntryType.AbsolutePath);
            return new AbsolutePath(m_data);
        }

        /// <summary>
        /// Returns the current value as an integer.
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where <see cref="EntryType" /> is equal to <see cref="PipDataEntryType.NestedDataStart" />, or
        /// <see cref="PipDataEntryType.NestedDataEnd" />.
        /// </remarks>
        /// <returns>Value as integer.</returns>
        public int GetIntegralValue()
        {
            Contract.Requires(
                EntryType == PipDataEntryType.NestedDataStart ||
                EntryType == PipDataEntryType.NestedDataEnd);
            return m_data;
        }

        /// <summary>
        /// Returns the current value as a string id.
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where <see cref="EntryType" /> is equal to <see cref="PipDataEntryType.StringLiteral" /> or
        /// <see cref="PipDataEntryType.NestedDataHeader" />.
        /// </remarks>
        /// <returns>Value as string id</returns>
        [Pure]
        public StringId GetStringValue()
        {
            Contract.Requires(
                EntryType == PipDataEntryType.StringLiteral ||
                EntryType == PipDataEntryType.NestedDataHeader);
            return new StringId(m_data);
        }

        #region Conversions

        /// <summary>
        /// Creates a nested data header entry
        /// </summary>
        internal static PipDataEntry CreateNestedDataHeader(PipDataFragmentEscaping escaping, StringId separator)
        {
            return new PipDataEntry(escaping, PipDataEntryType.NestedDataHeader, separator.Value);
        }

        /// <summary>
        /// Creates a nested data header entry
        /// </summary>
        internal static PipDataEntry CreateNestedDataStart(int entryLength)
        {
            return new PipDataEntry(PipDataFragmentEscaping.Invalid, PipDataEntryType.NestedDataStart, entryLength);
        }

        /// <summary>
        /// Creates a nested data header entry
        /// </summary>
        internal static PipDataEntry CreateNestedDataEnd(int fragmentCount)
        {
            return new PipDataEntry(PipDataFragmentEscaping.Invalid, PipDataEntryType.NestedDataEnd, fragmentCount);
        }

        /// <summary>
        /// Implicitly convert a string to a string PipFragment.
        /// </summary>
        public static implicit operator PipDataEntry(StringId data)
        {
            return new PipDataEntry(PipDataFragmentEscaping.Invalid, PipDataEntryType.StringLiteral, data.Value);
        }

        /// <summary>
        /// Implicitly convert a file artifact to a path PipFragment.
        /// </summary>
        public static implicit operator PipDataEntry(FileArtifact data)
        {
            return new PipDataEntry(PipDataFragmentEscaping.Invalid, PipDataEntryType.AbsolutePath, data.Path.RawValue);
        }

        /// <summary>
        /// Implicitly convert a path to a path PipFragment.
        /// </summary>
        public static implicit operator PipDataEntry(AbsolutePath data)
        {
            return new PipDataEntry(PipDataFragmentEscaping.Invalid, PipDataEntryType.AbsolutePath, data.RawValue);
        }
        #endregion

        #region Serialization

        public void Write(byte[] bytes, ref int index)
        {
            bytes[index++] = checked((byte)(((int)EntryType << 4) | (int)m_escaping));
            Bits.WriteInt32(bytes, ref index, m_data);
        }

        public static PipDataEntry Read<TBytes>(TBytes bytes, ref int index)
            where TBytes : IReadOnlyList<byte>
        {
            var b = bytes[index++];
            var entryType = (PipDataEntryType)(b >> 4);
            Contract.Assume((b & 15) <= (int)PipDataFragmentEscaping.CRuntimeArgumentRules);
            var escaping = (PipDataFragmentEscaping)(b & 15);

            var data = Bits.ReadInt32(bytes, ref index);
            return new PipDataEntry(escaping, entryType, data);
        }
        #endregion

        #region IEquatable<PipDataEntry> implementation

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine((int)EntryType, (int)m_escaping, m_data);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(PipDataEntry other)
        {
            return other.EntryType == EntryType &&
                other.m_data == m_data &&
                other.m_escaping == m_escaping;
        }

        public static bool operator ==(PipDataEntry left, PipDataEntry right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PipDataEntry left, PipDataEntry right)
        {
            return !(left == right);
        }
        #endregion
    }
}
