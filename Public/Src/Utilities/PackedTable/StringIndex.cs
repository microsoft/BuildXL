// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Sorts a list of StringIds, and allows accessing the sort order.
    /// </summary>
    public class StringIndex : IComparer<StringId>
    {
        private class StringIdComparer : IComparer<StringId>
        {
            private readonly StringTable m_stringTable;
            public StringIdComparer(StringTable stringTable)
            {
                m_stringTable = stringTable;
            }
            public int Compare(StringId x, StringId y)
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

                ReadOnlySpan<char> xSpan = m_stringTable[x];
                ReadOnlySpan<char> ySpan = m_stringTable[y];
                return xSpan.CompareTo(ySpan, StringComparison.InvariantCulture);
            }
        }

        private readonly StringTable m_stringTable;

        private readonly StringId[] m_sortedStringIds;

        /// <summary>
        /// List indexed by StringId value, containing the sort ordering of each StringId.
        /// </summary>
        private readonly int[] m_stringIdSortOrder;

        /// <summary>
        /// Construct an index over the given table.
        /// </summary>
        public StringIndex(StringTable stringTable)
        {
            m_stringTable = stringTable;

            // First, we sort all the strings in the StringTable, resulting in a list of StringIds
            // in the sorted order of their strings.
            // Then we invert that relation, giving us a list of sort-order values indexed by StringId.
            // We then use that list when doing StringId comparisons.

            // list of all string IDs in sorted order
            m_sortedStringIds = new StringId[stringTable.Count + 1];

            int i;
            for (i = 1; i < stringTable.Count + 1; i++)
            {
                m_sortedStringIds[i] = new StringId(i);
            }

            // Sort this in parallel since it's the biggest table and takes the longest.
            StringIdComparer stringIdComparer = new StringIdComparer(stringTable);
            Memory<StringId> sortedStringIdMemory = m_sortedStringIds.AsMemory();
            sortedStringIdMemory.ParallelSort(stringIdComparer);

            // list of the sort order for each string ID
            m_stringIdSortOrder = new int[stringTable.Count + 1];
            
            for (i = 0; i < m_sortedStringIds.Length; i++)
            {
                m_stringIdSortOrder[m_sortedStringIds[i].Value] = i;
            }
        }

        /// <summary>
        /// Get the sort order for this StringId (relative to all other StringIds known to this object).
        /// </summary>
        /// <returns>the sorted order of the string with this ID</returns>
        public int this[StringId stringId] => m_stringIdSortOrder[stringId.Value];

        /// <summary>
        /// Compare two StringIds based on their sort order.
        /// </summary>
        public int Compare(StringId x, StringId y) => this[x].CompareTo(this[y]);

        /// <summary>
        /// This comparer compares indices into stringIdSortOrder, with the reserved FindIndex
        /// equating to the string we're looking for.
        /// </summary>
        /// <remarks>
        /// Basically this lets us use the BinarySearch method on the stringIdSortOrder list,
        /// so we can binary search through the 
        /// </remarks>
        private class FindComparer : IComparer<StringId>
        {
            internal static readonly StringId FindId = new StringId(int.MaxValue);
            private readonly StringIndex m_parent;
            private readonly string m_toFind;
            private readonly StringComparison m_stringComparison;

            internal FindComparer(
                StringIndex parent,
                string toFind, 
                StringComparison stringComparison = StringComparison.InvariantCulture)
            {
                m_parent = parent;
                m_toFind = toFind;
                m_stringComparison = stringComparison;
            }

            public int Compare(StringId x, StringId y)
            {
                bool xDefault = x == default;
                bool yDefault = y == default;
                if (xDefault && yDefault)
                {
                    return 0;
                }
                if (xDefault)
                {
                    return 1;
                }
                if (yDefault)
                {
                    return -1;
                }

                ReadOnlySpan<char> xSpan = x == FindId ? m_toFind : m_parent.m_stringTable[x];
                ReadOnlySpan<char> ySpan = y == FindId ? m_toFind : m_parent.m_stringTable[y];
                return xSpan.CompareTo(ySpan, m_stringComparison);
            }
        }

        /// <summary>
        /// Try to find this string in the table; return default if not found.
        /// </summary>
        public StringId Find(string toFind)
        {
            int index = Array.BinarySearch(m_sortedStringIds, FindComparer.FindId, new FindComparer(this, toFind));
            return index >= 0 ? m_sortedStringIds[index] : default;
        }
    }
}
