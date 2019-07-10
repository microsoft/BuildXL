// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// A factory for creating PipData objects
    /// </summary>
    /// <remarks>
    /// Representation of PipData is now a sequence of <see cref="PipDataEntry"/> values.
    /// Each value is comprised of a single byte code and a 4 byte integer <see cref="PipDataEntry.m_data"/>.
    /// The code stores the <see cref="PipDataEntryType"/> and optionally the <see cref="PipDataFragmentEscaping"/>.
    ///
    /// Entry types and interpretation:
    ///
    /// EntryType                 |                 Data Type                    | Usage
    /// --------------------------|----------------------------------------------|----------------------------------------------
    /// Invalid                   |                   N/A                        | Represents uninitialized PipDataEntry
    /// --------------------------|----------------------------------------------|----------------------------------------------
    /// StringLiteral             |  <see cref="StringId.Value"/>                | Represents string value
    /// --------------------------|----------------------------------------------|----------------------------------------------
    /// AbsolutePath              | <see cref="AbsolutePath.RawValue"/>          | Represents full path value
    /// --------------------------|----------------------------------------------|----------------------------------------------
    /// IpcMoniker                | <see cref="IIpcMoniker.Id"/>                 | Represents id of an IPC moniker
    /// --------------------------|----------------------------------------------|----------------------------------------------
    /// VsoHashEntry1Path         | <see cref="AbsolutePath.RawValue"/>          | First entry of a <see cref="PipFragmentType.VsoHash"/> fragment.
    ///                           |                                              | Its data corresponds to the <see cref="FileArtifact.Path"/> property.
    /// --------------------------|----------------------------------------------|----------------------------------------------
    /// VsoHashEntry2RewriteCount | <see cref="FileArtifact.RewriteCount"/>      | Second entry of a <see cref="PipFragmentType.VsoHash"/> fragment.
    ///                           |                                              | Its data holds the <see cref="FileArtifact.RewriteCount"/> property.
    /// --------------------------|----------------------------------------------|----------------------------------------------
    /// NestedDataHeader          | <see cref="StringId.Value"/>                 | Represents the start of a nested pip data.
    ///                           |                                              | Data represents separator between fragments of
    ///                           |                                              | nested pip data. The next entry should be a
    ///                           |                                              | NestedDataStart entry whose data is the length
    ///                           |                                              | of the nested data span (i.e., the number of entries
    ///                           |                                              | excluding the header (including start and end
    ///                           |                                              | entry) which make up the nested data and child
    ///                           |                                              | fragments of the nested data entry. This is
    ///                           |                                              | essential a pointer to the last entry.
    /// --------------------------|----------------------------------------------|----------------------------------------------
    /// NestedDataStart           | <see cref="int"/>                          | Data is number entries in nested pip data excluding header.
    ///                           |                                              | The nested data end entry is located at (NestedDataStartIndex + Data - 1).
    /// --------------------------|----------------------------------------------|----------------------------------------------
    /// NestedDataEnd             | <see cref="int"/>                          | Data is number of fragments in nested pip data excluding header).
    /// --------------------------|----------------------------------------------|----------------------------------------------
    /// </remarks>
    public sealed class PipDataBuilder
    {
        private readonly List<PipDataEntry> m_entries = new List<PipDataEntry>();
        private byte[] m_entriesBinaryStringBuffer = new byte[32];
        internal readonly StringTable StringTable;
        private PipDataCountInfo m_currentPipDataCountInfo;
        private readonly Stack<PipDataCountInfo> m_nestedDataFragmentCountStack = new Stack<PipDataCountInfo>();

        /// <summary>
        /// Creates a new PipDataBuilder
        /// </summary>
        /// <param name="stringTable">the string table to use for storing strings</param>
        public PipDataBuilder(StringTable stringTable)
        {
            Contract.Requires(stringTable != null);

            StringTable = stringTable;
            Clear();
        }

        /// <summary>
        /// Returns the PipDataBuilder back to its initial state.
        /// </summary>
        public void Clear()
        {
            m_entries.Clear();

            // Add entry which will be replaced with the count of the number of contained entries
            m_entries.Add(PipDataEntry.CreateNestedDataStart(0));
            m_nestedDataFragmentCountStack.Clear();
            m_currentPipDataCountInfo = default(PipDataCountInfo);
        }

        /// <summary>
        /// Creates a pip fragment representing the pip data.
        /// </summary>
        internal static PipFragment AsPipFragment(in PipData pipData)
        {
            Contract.Requires(pipData.IsValid);

            var entries = new PipDataEntry[pipData.Entries.Count + 1];
            entries[0] = pipData.HeaderEntry;

            int i = 1;
            foreach (var entry in pipData.Entries)
            {
                entries[i++] = entry;
            }

            return new PipFragment(PipDataEntryList.FromEntries(entries), 0);
        }

        /// <summary>
        /// Creates a new pip data from the pip data builder beginning at the fragment specified by the cursor
        /// </summary>
        public PipData ToPipData(string separator, PipDataFragmentEscaping escaping, Cursor startMarker = default(Cursor))
        {
            Contract.Requires(separator != null);
            Contract.Requires(escaping != PipDataFragmentEscaping.Invalid);

            return ToPipData(StringId.Create(StringTable, separator), escaping, startMarker);
        }

        /// <summary>
        /// Creates a new pip data from the pip data builder beginning at the fragment specified by the cursor
        /// </summary>
        public PipData ToPipData(StringId separator, PipDataFragmentEscaping escaping, Cursor startMarker = default(Cursor))
        {
            Contract.Requires(separator.IsValid);
            Contract.Requires(escaping != PipDataFragmentEscaping.Invalid);

            // If start index is 0, this cursor captures the full pip data. Therefore the start index of the
            // first fragment is 1.
            var startIndexOfFirstFragment = startMarker.StartEntryIndex == 0 ? 1 : startMarker.StartEntryIndex;

            // Add 2 for start and end entries
            var entryLength = m_entries.Count - startIndexOfFirstFragment + 2;
            var entriesBinarySegment = WriteEntries(getEntries(), entryLength, ref m_entriesBinaryStringBuffer);

            // NOTE: The raw value is added to string table backing byte buffers without being converted to a CLR string object
            var pipDataId = StringTable.AddString(entriesBinarySegment);

            return PipData.CreateInternal(
                PipDataEntry.CreateNestedDataHeader(escaping, separator),
                new PipDataEntryList(StringTable.GetBytes(pipDataId)),
                pipDataId);

            IEnumerable<PipDataEntry> getEntries()
            {
                yield return PipDataEntry.CreateNestedDataStart(entryLength);

                for(int i = startIndexOfFirstFragment; i < m_entries.Count; i++)
                {
                    yield return m_entries[i];
                }

                var fragmentCount = m_currentPipDataCountInfo.FragmentCount - startMarker.PrecedingFragmentCount;
                yield return PipDataEntry.CreateNestedDataEnd(fragmentCount);
            }
        }

        internal static BinaryStringSegment WriteEntries(IEnumerable<PipDataEntry> entries, int entryLength, ref byte[] buffer)
        {
            var entryBytesLength = entryLength * PipDataEntry.BinarySize;
            CollectionUtilities.GrowArrayIfNecessary(ref buffer, entryBytesLength);

            int byteIndex = 0;
            foreach (var entry in entries)
            {
                entry.Write(buffer, ref byteIndex);
            }

            Contract.Assert(byteIndex == entryBytesLength);
            if ((byteIndex % 2) != 0)
            {
                // Need even number of bytes for UTF-16 string
                byteIndex++;
            }

            // Convert the bytes to a binary string segment (i.e. represent raw entry bytes as UTF-16 string)
            return new BinaryStringSegment(buffer, 0, byteIndex, isAscii: false);
        }

        /// <summary>
        /// Creates a new pip data from the given atoms
        /// </summary>
        public static PipData CreatePipData(StringTable stringTable, string separator, PipDataFragmentEscaping escaping, params PipDataAtom[] atoms)
        {
            Contract.Requires(stringTable != null);
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, atom => atom.IsValid);
            Contract.Requires(separator != null);
            Contract.Requires(escaping != PipDataFragmentEscaping.Invalid);

            var pdb = new PipDataBuilder(stringTable);
            pdb.AddRange(atoms);
            return pdb.ToPipData(separator, escaping);
        }

        /// <summary>
        /// Creates a new pip data from a file
        /// </summary>
        public static PipData CreatePipData(AbsolutePath path)
        {
            return PipData.CreateInternal(
                PipDataEntry.CreateNestedDataHeader(PipDataFragmentEscaping.NoEscaping, StringId.Invalid),
                PipDataEntryList.FromEntries(new[] { (PipDataEntry)path }),
                StringId.Invalid);
        }

        /// <summary>
        /// Trims the end fragments from the pip data builder
        /// </summary>
        /// <param name="startMarker">the marker of the first argument to remove</param>
        public void TrimEnd(Cursor startMarker)
        {
            // If start index is 0, this cursor captures the full pip data. Therefore the start index of the
            // first fragment is 1.
            var startIndexOfFirstFragment = startMarker.StartEntryIndex == 0 ? 1 : startMarker.StartEntryIndex;

            // Cursor is after the end of the list. Don't remove anything.
            if (startMarker.StartEntryIndex >= m_entries.Count)
            {
                return;
            }

            // Remove trailing entries
            m_entries.RemoveRange(startIndexOfFirstFragment, m_entries.Count - startIndexOfFirstFragment);
            m_currentPipDataCountInfo.FragmentCount = startMarker.PrecedingFragmentCount;
        }

        /// <summary>
        /// Adds a path to the pip data
        /// </summary>
        public void Add(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            m_currentPipDataCountInfo.FragmentCount++;
            m_entries.Add(path);
        }

        /// <summary>
        /// Adds a VSO hash of a file to the pip data.
        /// </summary>
        public void AddVsoHash(FileArtifact file)
        {
            Contract.Requires(file.IsValid);

            m_currentPipDataCountInfo.FragmentCount++;
            PipDataEntry entry1;
            PipDataEntry entry2;
            PipDataEntry.CreateVsoHashEntry(file, out entry1, out entry2);
            m_entries.Add(entry1);
            m_entries.Add(entry2);
        }

        /// <summary>
        /// Adds an IpcMoniker to the pip data.
        /// </summary>
        public void AddIpcMoniker(IIpcMoniker moniker)
        {
            Contract.Requires(moniker != null);

            m_currentPipDataCountInfo.FragmentCount++;
            m_entries.Add(PipDataEntry.CreateIpcMonikerEntry(moniker, StringTable));
        }

        /// <summary>
        /// Adds a path to the pip data
        /// </summary>
        public void AddRange(IEnumerable<PipDataAtom> atoms)
        {
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, atom => atom.IsValid);

            foreach (var atom in atoms)
            {
                Add(atom);
            }
        }

        /// <summary>
        /// Adds a path to the pip data
        /// </summary>
        public void AddRange(params PipDataAtom[] atoms)
        {
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, atom => atom.IsValid);

            foreach (var atom in atoms)
            {
                Add(atom);
            }
        }

        /// <summary>
        /// Adds a string or path
        /// </summary>
        public void Add(PipDataAtom atom)
        {
            Contract.Requires(atom.IsValid);
            switch (atom.DataType)
            {
                case PipDataAtomType.String:
                case PipDataAtomType.StringId:
                    Add(atom.GetStringIdValue(StringTable));
                    break;

                // case PipDataAtomType.AbsolutePath:
                default:
                    Contract.Assert(atom.DataType == PipDataAtomType.AbsolutePath);
                    Add(atom.GetPathValue());
                    break;
            }
        }

        /// <summary>
        /// Adds a string to the pip data.
        /// </summary>
        public void Add(string literal)
        {
            Contract.Requires(literal != null);

            m_currentPipDataCountInfo.FragmentCount++;
            var id = StringId.Create(StringTable, literal);
            m_entries.Add(id);
        }

        /// <summary>
        /// Adds a string from
        /// </summary>
        public void Add(StringId value)
        {
            Contract.Requires(value.IsValid);

            m_currentPipDataCountInfo.FragmentCount++;
            m_entries.Add(value);
        }

        /// <summary>
        /// Adds a copy of the pip fragment to the pip data.
        /// </summary>
        public void Add(PipFragment fragment)
        {
            Contract.Requires(fragment.FragmentType != PipFragmentType.Invalid);

            switch (fragment.FragmentType)
            {
                case PipFragmentType.StringLiteral:
                    Add(fragment.GetStringIdValue());
                    break;
                case PipFragmentType.AbsolutePath:
                    Add(fragment.GetPathValue());
                    break;
                default:
                    Contract.Assert(fragment.FragmentType == PipFragmentType.NestedFragment);
                    Add(fragment.GetNestedFragmentValue());
                    break;
            }
        }

        /// <summary>
        /// Adds a copy of the given pip data to the pip data.
        /// </summary>
        public void Add(in PipData pipData)
        {
            Contract.Requires(pipData.IsValid);

            m_currentPipDataCountInfo.FragmentCount++;
            m_entries.Add(pipData.HeaderEntry);
            m_entries.AddRange(pipData.Entries);
        }

        /// <summary>
        /// Starts a new nested pip data scope.
        /// </summary>
        /// <param name="escaping">the escaping of the nested pip data.</param>
        /// <param name="separator">the separator of the nested pip data</param>
        /// <returns>A disposable which closes the scope on Dispose.</returns>
        public FragmentScope StartFragment(PipDataFragmentEscaping escaping, string separator)
        {
            Contract.Requires(escaping != PipDataFragmentEscaping.Invalid);
            Contract.Requires(separator != null);

            return StartFragment(escaping, StringId.Create(StringTable, separator));
        }

        /// <summary>
        /// Starts a new nested pip data scope.
        /// </summary>
        /// <param name="escaping">the escaping of the nested pip data.</param>
        /// <param name="separator">the separator of the nested pip data</param>
        /// <returns>A disposable which closes the scope on Dispose.</returns>
        public FragmentScope StartFragment(PipDataFragmentEscaping escaping, StringId separator)
        {
            Contract.Requires(escaping != PipDataFragmentEscaping.Invalid);
            Contract.Requires(separator.IsValid);

            m_currentPipDataCountInfo.FragmentCount++;
            m_nestedDataFragmentCountStack.Push(m_currentPipDataCountInfo);
            AddPipDataHeader(escaping, separator);
            m_currentPipDataCountInfo = new PipDataCountInfo { StartEntryIndex = m_entries.Count };

            // Add entry which will be replaced with the count of the number of contained entries
            m_entries.Add(PipDataEntry.CreateNestedDataStart(0));

            return new FragmentScope(this);
        }

        private void AddPipDataHeader(PipDataFragmentEscaping escaping, StringId separator)
        {
            m_entries.Add(PipDataEntry.CreateNestedDataHeader(escaping, separator));
        }

        /// <summary>
        /// Closes the current fragment scope.
        /// </summary>
        public void EndFragment()
        {
            AddEndNestedData();
            m_currentPipDataCountInfo = m_nestedDataFragmentCountStack.Pop();
        }

        /// <summary>
        /// Creates a cursor which can be used to create pip data which a subset of the fragments in the builder
        /// </summary>
        public Cursor CreateCursor()
        {
            return new Cursor { StartEntryIndex = m_entries.Count, PrecedingFragmentCount = m_currentPipDataCountInfo.FragmentCount };
        }

        private void AddEndNestedData()
        {
            m_entries.Add(PipDataEntry.CreateNestedDataEnd(m_currentPipDataCountInfo.FragmentCount));
            m_entries[m_currentPipDataCountInfo.StartEntryIndex] = PipDataEntry.CreateNestedDataStart(m_entries.Count - m_currentPipDataCountInfo.StartEntryIndex);
        }

        private static void CompleteNestedData(PipDataEntry[] entries, PipDataCountInfo pipdataCountInfo)
        {
            entries[entries.Length - 1] = PipDataEntry.CreateNestedDataEnd(pipdataCountInfo.FragmentCount);
            entries[0] = PipDataEntry.CreateNestedDataStart(entries.Length);
        }

        /// <summary>
        /// Tracks information about the number of fragments for a nested PipData. and the location of the start entry
        /// </summary>
        private struct PipDataCountInfo
        {
            public int FragmentCount;
            public int StartEntryIndex;
        }

        /// <summary>
        /// Marker into originating PipDataBuilder used when splitting the PipData
        /// </summary>
        public struct Cursor
        {
            /// <summary>
            /// Default cursor at the beginning of the <see cref="PipDataBuilder"/>
            /// </summary>
            public static readonly Cursor Default = default(Cursor);

            internal int StartEntryIndex;
            internal int PrecedingFragmentCount;

            /// <summary>
            /// Gets whether the current cursor is the Default cursor at the beginning of the <see cref="PipDataBuilder"/>
            /// </summary>
            public bool IsDefault => StartEntryIndex == 0;
        }

        /// <summary>
        /// A disposable which closes a nested pip data scope on Dispose by writing the end entry.
        /// </summary>
        public readonly struct FragmentScope : IDisposable
        {
            private readonly PipDataBuilder m_pipDataBuilder;

            internal FragmentScope(PipDataBuilder pipDataBuilder)
            {
                m_pipDataBuilder = pipDataBuilder;
            }

            /// <summary>
            /// Inserts an NestedDataEnd entry for the current nested pip data scope
            /// </summary>
            public void Dispose()
            {
                m_pipDataBuilder.EndFragment();
            }
        }
    }
}
