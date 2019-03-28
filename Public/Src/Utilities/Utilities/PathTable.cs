// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Optimized table of file system paths.
    /// </summary>
    /// <remarks>
    /// Individual paths are inserted into the table and the caller receives an AbsolutePath as a handle
    /// to the path. Later, the AbsolutePath can be turned back into a path.
    /// This data structure only ever grows, paths are never removed. The entire abstraction is completely
    /// thread-safe.
    /// When all insertions have been done, the table can be frozen, which discards some transient state and
    /// cuts heap consumption to a minimum. Trying to add a new path once the table has been frozen will crash.
    /// </remarks>
    public sealed class PathTable : HierarchicalNameTable
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "PathTable", version: 0);

        /// <summary>
        /// Comparer of <see cref="AbsolutePath"/>s, as if they were expanded.
        /// </summary>
        public readonly ExpandedAbsolutePathComparer ExpandedPathComparer;

        /// <summary>
        /// Initializes a new path table.
        /// </summary>
        public PathTable(StringTable stringTable)
            : base(stringTable, ignoreCase: true, separator: System.IO.Path.DirectorySeparatorChar)
        {
            Contract.Requires(stringTable != null);
            ExpandedPathComparer = new ExpandedAbsolutePathComparer(ExpandedNameComparer);
        }

        /// <summary>
        /// Initializes a new path table with a private string table.
        /// </summary>
        public PathTable(bool disableDebugTag = false)
            : base(new StringTable(), ignoreCase: true, disableDebugTag: disableDebugTag, separator: System.IO.Path.DirectorySeparatorChar)
        {
            ExpandedPathComparer = new ExpandedAbsolutePathComparer(ExpandedNameComparer);
        }

        private PathTable(SerializedState state, StringTable stringTable)
            : base(state, stringTable, true, System.IO.Path.DirectorySeparatorChar)
        {
            ExpandedPathComparer = new ExpandedAbsolutePathComparer(ExpandedNameComparer);
        }

        /// <summary>
        /// Comparer for sorting paths as if they were expanded, but without actually expanding them.
        /// Comparison performs at most one string-comparison of name components.
        /// </summary>
        public sealed class ExpandedAbsolutePathComparer : IComparer<AbsolutePath>
        {
            private readonly ExpandedHierarchicalNameComparer m_comparer;

            internal ExpandedAbsolutePathComparer(ExpandedHierarchicalNameComparer comparer)
            {
                Contract.Requires(comparer != null);
                m_comparer = comparer;
            }

            /// <inheritdoc />
            public int Compare(AbsolutePath x, AbsolutePath y)
            {
                return m_comparer.Compare(x.Value, y.Value);
            }
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        /// <remarks>
        /// Returns null if the StringTable task returns null, or if it turns out that hash codes no longer match.
        /// </remarks>
        public static async Task<PathTable> DeserializeAsync(BuildXLReader reader, Task<StringTable> stringTableTask)
        {
            Contract.Requires(reader != null);
            Contract.Requires(stringTableTask != null);

            var state = await ReadSerializationStateAsync(reader, stringTableTask);
            var stringTable = await stringTableTask;
            if (state != null && stringTable != null)
            {
                return new PathTable(state, stringTable);
            }

            return null;
        }

        /// <summary>
        /// Sets PathTable used for path expansion (for use during testing)
        /// </summary>
        internal static PathTable DebugPathTable { get; set; }

        /// <summary>
        /// Imports the given path table into the current path table and returns
        /// a mapping from the other path table ids to the current path table's absolute paths
        /// </summary>
        public AbsolutePath[] Import(PathTable otherTable)
        {
            var importedPathIndex = new AbsolutePath[otherTable.Count];
            var nameBuffer = new char[1024];

            for (int i = 1; i < otherTable.Count; i++)
            {
                var otherChildPath = new AbsolutePath(i);
                var otherParent = otherChildPath.GetParent(otherTable);

                // The contract here is the parent's id is always smaller than its child's id.
                // Thus, importPathIndex below is properly initialized.
                var importedParent = importedPathIndex[otherParent.Value.Index];

                var otherChildName = otherChildPath.GetName(otherTable);
                int length = otherTable.StringTable.CopyString(otherChildName.StringId, ref nameBuffer, 0, allowResizeBuffer: true);
                var component = StringTable.AddString(new CharArraySegment(nameBuffer, 0, length));
                var child = AddComponent(importedParent.Value, component);
                importedPathIndex[i] = new AbsolutePath(child);
            }

            return importedPathIndex;
        }
    }
}
