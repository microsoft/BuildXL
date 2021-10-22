// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>Flatten the names in the table, such that each atom in each name is O(1) accessible;
    /// optionally also build a sorted name index.</summary>
    /// <remarks>
    /// The base NameTable representation is basically a "linked list" of atoms (a suffix
    /// table). This "index" flattens the list, such that each NameId maps to the list of
    /// in-order StringIds directly.
    /// This trades more memory usage for faster O(1) lookup of any atom in a name.
    /// </remarks>
    public class NameIndex : MultiValueTable<NameId, NameEntry>, IComparer<NameId>
    {
        private const int MaximumNameLength = 100;

        /// <summary>For each name, its sort order.</summary>
        private SingleValueTable<NameId, int> m_nameSortOrder;

        /// <summary>Construct a NameIndex over a base table of names.</summary>
        public NameIndex(NameTable baseTable) : base(baseTable)
        {
            NameEntry[] entryArray = new NameEntry[MaximumNameLength];
            foreach (NameId id in baseTable.Ids)
            {
                // The length of each name is one longer than the length of its prefix.
                // Since the name table is constructed in prefix order (e.g. the prefix of a name
                // must always already exist before a suffix referencing that prefix can be added),
                // we can rely on the atom length of the prefix already being in this table.
                NameEntry entry = baseTable[id];

                if (entry.Prefix == default)
                {
                    // this is a root name
                    entryArray[0] = entry;
                    Add(entryArray.AsSpan(0, 1));
                }
                else
                {
                    // copy from the existing prefix
                    ReadOnlySpan<NameEntry> prefixEntries = this[entry.Prefix];
                    prefixEntries.CopyTo(entryArray.AsSpan());
                    // add this atom
                    entryArray[prefixEntries.Length] = entry;
                    Add(entryArray.AsSpan(0, prefixEntries.Length + 1));
                }
            }
        }

        /// <summary>The underlying NameTable indexed by this object.</summary>
        public NameTable NameTable => (NameTable)BaseTableOpt;

        /// <summary>Internal comparer used during sorting.</summary>
        private class NameSortComparer : IComparer<NameId>
        {
            private readonly NameIndex m_nameIndex;
            private readonly StringIndex m_stringIndex;
            internal NameSortComparer(NameIndex nameIndex, StringIndex stringIndex)
            {
                m_nameIndex = nameIndex;
                m_stringIndex = stringIndex;
            }

            public int Compare(NameId x, NameId y)
            {
                if (x == default)
                {
                    if (y == default)
                    {
                        return 0;
                    }
                    else
                    {
                        return -1;
                    }
                }
                else if (y == default)
                {
                    return 1;
                }
                int minLength = Math.Min(m_nameIndex[x].Length, m_nameIndex[y].Length);
                for (int i = 0; i < minLength; i++)
                {
                    int atomComparison = m_stringIndex.Compare(m_nameIndex[x][i].Atom, m_nameIndex[y][i].Atom);
                    if (atomComparison != 0)
                    {
                        return atomComparison;
                    }
                }
                return m_nameIndex[x].Length.CompareTo(m_nameIndex[y].Length);
            }
        }

        /// <summary>Sort this NameIndex.</summary>
        /// <remarks>Sorting at the name level isn't needed by all apps, so it is optional;
        /// after calling this, HasBeenSorted becomes true and Compare can be called.</remarks>
        public void Sort(StringIndex stringIndex)
        {
            if (HasBeenSorted)
            {
                // only need to sort once
                return;
            }

            NameId[] ids = new NameId[BaseTableOpt.Count + 1];
            for (int i = 1; i <= BaseTableOpt.Count; i++)
            {
                ids[i] = new NameId(i);
            }

            NameSortComparer comparer = new NameSortComparer(this, stringIndex);
            ids.AsMemory().ParallelSort(comparer);

            // build out the table of sort orders by id
            m_nameSortOrder = new SingleValueTable<NameId, int>(NameTable);
            m_nameSortOrder.FillToBaseTableCount();
            for (int i = 1; i < ids.Length; i++)
            {
                m_nameSortOrder[ids[i]] = i;
            }
        }

        /// <summary>True iff this NameIndex has had Sort() called on it.</summary>
        public bool HasBeenSorted => m_nameSortOrder != null;

        /// <summary>Compare two NameIds from this index; requires HasBeenSorted.</summary>
        public int Compare(NameId x, NameId y)
        {
            if (!HasBeenSorted)
            {
                throw new InvalidOperationException("Must call Sort() on NameIndex before calling Compare()");
            }
            return m_nameSortOrder[x].CompareTo(m_nameSortOrder[y]);
        }
    }
}
