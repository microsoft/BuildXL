// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedTable;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>
    /// Boilerplate ID type to avoid ID confusion in code.
    /// </summary>
    public struct WorkerId : Id<WorkerId>, IEqualityComparer<WorkerId>
    {
        /// <summary>Value as int.</summary>
        public readonly int Value;
        /// <summary>Constructor.</summary>
        public WorkerId(int value) { Value = value; }
        /// <summary>Eliminator.</summary>
        public int FromId() => Value;
        /// <summary>Introducer.</summary>
        public WorkerId ToId(int value) => new WorkerId(value);
        /// <summary>Debugging.</summary>
        public override string ToString() => $"WorkerId[{Value}]";
        /// <summary>Comparison.</summary>
        public bool Equals(WorkerId x, WorkerId y) => x.Value == y.Value;
        /// <summary>Hashing.</summary>
        public int GetHashCode(WorkerId obj) => obj.Value;
    }

    /// <summary>
    /// Tracks the workers in a build.
    /// </summary>
    /// <remarks>
    /// The StringId value is the worker's MachineName.
    /// </remarks>
    public class WorkerTable : SingleValueTable<WorkerId, PackedTable.StringId>
    {
        /// <summary>
        /// The table containing the strings referenced by this WorkerTable.
        /// </summary>
        /// <remarks>
        /// The WorkerTable does not own this StringTable; it is probably shared.
        /// </remarks>
        public readonly PackedTable.StringTable StringTable;

        /// <summary>
        /// Construct a WorkerTable.
        /// </summary>
        public WorkerTable(PackedTable.StringTable stringTable, int capacity = DefaultCapacity) : base(capacity)
        {
            StringTable = stringTable;
        }

        /// <summary>
        /// Build a WorkerTable by caching worker machine names.
        /// </summary>
        public class CachingBuilder : CachingBuilder<PackedTable.StringId>
        {
            private readonly PackedTable.StringTable.CachingBuilder m_stringTableBuilder;

            /// <summary>
            /// Construct a CachingBuilder.
            /// </summary>
            public CachingBuilder(WorkerTable workerTable, PackedTable.StringTable.CachingBuilder stringTableBuilder) 
                : base(workerTable)
            {
                m_stringTableBuilder = stringTableBuilder;
            }

            /// <summary>
            /// Get or add the WorkerId for the given worker machine name.
            /// </summary>
            public WorkerId GetOrAdd(string workerMachineName)
            {
                PackedTable.StringId stringId = m_stringTableBuilder.GetOrAdd(workerMachineName);
                return GetOrAdd(stringId);
            }
        }
    }
}
