// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents a set of <see cref="HierarchicalNameId" />s which can be used to perform queries
    /// on a <see cref="HierarchicalNameTable" />.
    /// </summary>
    /// <remarks>
    /// Names added to the set are flagged with <see cref="HierarchicalNameTable.SetFlags" />
    /// as a first-level filter. Since that is global to the table, it admits false positives.
    /// As a second-level filter, this implementation tracks those names actually added.
    /// This implementation is thread-safe.
    /// </remarks>
    public sealed class FlaggedHierarchicalNameSet
    {
        private readonly HierarchicalNameTable m_nameTable;
        private readonly HierarchicalNameTable.NameFlags m_memberFlags;
        private readonly ConcurrentDictionary<HierarchicalNameId, Unit> m_added = new ConcurrentDictionary<HierarchicalNameId, Unit>();

        /// <summary>
        /// Creates a name set which contains some subset of those that have all of <paramref name="flags" /> set.
        /// </summary>
        public FlaggedHierarchicalNameSet(HierarchicalNameTable nameTable, HierarchicalNameTable.NameFlags flags)
        {
            Contract.Requires(nameTable != null);
            m_nameTable = nameTable;
            m_memberFlags = flags;
        }

        /// <summary>
        /// Ensures that the given name is in this set.
        /// </summary>
        public void Add(HierarchicalNameId name)
        {
            Contract.Requires(name.IsValid);

            if (m_added.TryAdd(name, Unit.Void))
            {
                // TODO: Since we can atomically know the set of previous flags, it would be much nicer if we avoided
                //       using m_added at all in the common case (i.e., only a single scheduler instance is using flags)
                Analysis.IgnoreResult(m_nameTable.SetFlags(name, m_memberFlags));
            }
        }

        /// <summary>
        /// Finds the first name in the hierarchy of <paramref name="name" /> (bottom-up) that is in this set.
        /// If no match is found, <see cref="HierarchicalNameId.Invalid"/> is returned.
        /// </summary>
        public HierarchicalNameId FindInHierarchyBottomUp(HierarchicalNameId name)
        {
            foreach (HierarchicalNameId current in m_nameTable.EnumerateHierarchyBottomUp(name, flagsFilter: m_memberFlags))
            {
                if (m_added.ContainsKey(current))
                {
                    return current;
                }
            }

            return HierarchicalNameId.Invalid;
        }
    }
}
