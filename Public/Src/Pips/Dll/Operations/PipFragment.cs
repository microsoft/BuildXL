// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Represents a snippet of dynamic data for execution.
    /// </summary>
    /// <remarks>
    /// This can contain paths or string literals.
    /// </remarks>
    public readonly struct PipFragment : IEquatable<PipFragment>
    {
        private readonly PipDataEntryList m_entries;

        /// <summary>
        /// Internal constructor, please use CreateSourceFile.... overloads to instantiate this type.
        /// </summary>
        internal PipFragment(PipDataEntryList entries, int index)
        {
            // Ensure the entry can be accessed from the array
            Contract.Requires(entries.Count != 0);
            Contract.Requires(index >= 0 && index < entries.Count);

            m_entries = entries.GetSubView(index);
        }

        /// <summary>
        /// Private constructor for test pip fragments
        /// </summary>
        private PipFragment(PipDataEntry[] entries, int index)
            : this(PipDataEntryList.FromEntries(entries), index)
        {
        }

        internal PipDataEntry Entry => m_entries[0];

        /// <summary>
        /// Whether this pip fragment is valid (and not the default value)
        /// </summary>
        public bool IsValid => FragmentType != PipFragmentType.Invalid;

        /// <summary>
        /// Exposes the type of this Fragment so consumers can choose which data to extract.
        /// </summary>
        public PipFragmentType FragmentType => Entry.FragmentType;

        #region Conversions

        /// <summary>
        /// Factory to create a PipFragment containing an absolute path.
        /// FOR TESTING PURPOSES ONLY.
        /// </summary>
        /// <param name="path">The absolute path to insert</param>
        /// <returns>Fragment with the given absolute path</returns>
        internal static PipFragment FromAbsolutePathForTesting(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            return new PipFragment(new PipDataEntry[] { path }, 0);
        }

        /// <summary>
        /// Factory to create a PipFragment containing a StringId.
        /// FOR TESTING PURPOSES ONLY.
        /// </summary>
        /// <param name="literal">The string literal to insert</param>
        /// <param name="stringTable">the associated string table</param>
        /// <returns>Fragment with a string literal</returns>
        internal static PipFragment FromString(string literal, StringTable stringTable)
        {
            Contract.Requires(literal != null);
            var literalId = StringId.Create(stringTable, literal);
            return new PipFragment(new PipDataEntry[] { literalId }, 0);
        }

        /// <summary>
        /// Factory to create a PipFragment containing a DescriptionData block.
        /// FOR TESTING PURPOSES ONLY.
        /// </summary>
        /// <param name="descriptionData">Description data to put as a fragment.</param>
        /// <returns>Fragment with a nested DescriptionData</returns>
        internal static PipFragment CreateNestedFragment(in PipData descriptionData)
        {
            Contract.Requires(descriptionData.IsValid);
            return PipDataBuilder.AsPipFragment(descriptionData);
        }

        /// <summary>
        /// Factory to create a PipFragment containing a VsoHash.
        /// FOR TESTING PURPOSES ONLY.
        /// </summary>
        internal static PipFragment VsoHashFromFileForTesting(FileArtifact file)
        {
            PipDataEntry.CreateVsoHashEntry(file, out var entry1, out var entry2);
            return new PipFragment(new[] { entry1, entry2 }, 0);
        }

        /// <summary>
        /// Factory to create a PipFragment containing an IpcMoniker.
        /// FOR TESTING PURPOSES ONLY.
        /// </summary>
        internal static PipFragment CreateIpcMonikerForTesting(IIpcMoniker moniker, StringTable stringTable)
        {
            var entry = PipDataEntry.CreateIpcMonikerEntry(moniker, stringTable);
            return new PipFragment(new[] { entry }, 0);
        }

        /// <summary>
        /// Returns the current value as FileArtifact;
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where Type is equal to PipDataFragmentType.AbsolutePath.
        /// </remarks>
        /// <returns>Value as a FileArtifact</returns>
        public AbsolutePath GetPathValue()
        {
            return Entry.GetPathValue();
        }

        /// <summary>
        /// Returns the current value as a string id.
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where Type is equal to PipDataFragmentType.StringLiteral.
        /// </remarks>
        /// <returns>Value as string id</returns>
        public StringId GetStringIdValue()
        {
            return Entry.GetStringValue();
        }

        /// <summary>
        /// Returns the current value as a FileArtifact.
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where Type is equal to <see cref="PipFragmentType.VsoHash"/> or <see cref="PipFragmentType.FileId"/>.
        /// </remarks>
        public FileArtifact GetFileValue()
        {
            Contract.Requires(FragmentType == PipFragmentType.VsoHash || FragmentType == PipFragmentType.FileId);

            Contract.Assert(m_entries.Count >= 2);
            var entry1 = m_entries[0];
            Contract.Assert(entry1.EntryType == PipDataEntryType.VsoHashEntry1Path || entry1.EntryType == PipDataEntryType.FileId1Path);
            var entry2 = m_entries[1];
            Contract.Assert(entry2.EntryType == PipDataEntryType.VsoHashEntry2RewriteCount || entry2.EntryType == PipDataEntryType.FileId2RewriteCount);
            return new FileArtifact(entry1.GetPathValue(), entry2.GetIntegralValue());
        }

        /// <summary>
        /// Returns the current value as a StringId corresponding to the <see cref="BuildXL.Ipc.Interfaces.IIpcMoniker.Id"/> IIpcMoniker property.
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where Type is equal to <see cref="PipFragmentType.IpcMoniker"/>.
        /// </remarks>
        public StringId GetIpcMonikerValue()
        {
            Contract.Requires(FragmentType == PipFragmentType.IpcMoniker);
            return Entry.GetStringValue();
        }

        /// <summary>
        /// Returns the current value as a pip data
        /// </summary>
        /// <remarks>
        /// You can only call this function for instances where Type is equal to PipDataFragmentType.NestedFragment.
        /// </remarks>
        /// <returns>Value as string</returns>
        [ContractVerification(false)]
        public PipData GetNestedFragmentValue()
        {
            Contract.Requires(FragmentType == PipFragmentType.NestedFragment);
            return PipData.CreateInternal(Entry, m_entries.GetSubView(1, m_entries[1].GetIntegralValue()), StringId.Invalid);
        }
        #endregion

        #region IEquatable<PipFragment> implementation

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <returns>
        /// true if the specified object is equal to the current object; otherwise, false.
        /// </returns>
        /// <param name="obj">The object to compare with the current object. </param>
        /// <filterpriority>2</filterpriority>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        /// <filterpriority>2</filterpriority>
        public bool Equals(PipFragment other)
        {
            if (!IsValid)
            {
                return !other.IsValid;
            }

            return other.IsValid && Entry.Equals(other.Entry);
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <returns>
        /// A hash code for the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override int GetHashCode()
        {
            return IsValid ? Entry.GetHashCode() : 0;
        }

        /// <summary>
        /// Indicates whether two object instances are equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare. </param>
        /// <param name="right">The second object to compare. </param>
        /// <filterpriority>3</filterpriority>
        public static bool operator ==(PipFragment left, PipFragment right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two objects instances are not equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are not equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <filterpriority>3</filterpriority>
        public static bool operator !=(PipFragment left, PipFragment right)
        {
            return !left.Equals(right);
        }
        #endregion
    }
}
