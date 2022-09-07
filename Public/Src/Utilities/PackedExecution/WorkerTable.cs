// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.PackedTable;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>
    /// Boilerplate ID type to avoid ID confusion in code.
    /// </summary>
    public readonly struct WorkerId : Id<WorkerId>, IEquatable<WorkerId>
    {
        /// <nodoc/>
        public readonly struct EqualityComparer : IEqualityComparer<WorkerId>
        {
            /// <nodoc/>
            public bool Equals(WorkerId x, WorkerId y) => x.Value == y.Value;
            /// <nodoc/>
            public int GetHashCode(WorkerId obj) => obj.Value;
        }

        /// <summary>A global comparer to avoid boxing allocation on each usage</summary>
        public static IEqualityComparer<WorkerId> EqualityComparerInstance { get; } = new EqualityComparer();

        /// <nodoc/>
        public int Value { get; }

        /// <nodoc/>
        public WorkerId(int value)
        {
            Id<WorkerId>.CheckValidId(value);
            Value = value;
        }
        /// <nodoc/>
        public WorkerId CreateFrom(int value) => new(value);

        /// <nodoc/>
        public override string ToString() => $"WorkerId[{Value}]";

        /// <nodoc/>
        public static bool operator ==(WorkerId x, WorkerId y) => x.Value == y.Value;

        /// <nodoc/>
        public static bool operator !=(WorkerId x, WorkerId y) => !(x == y);

        /// <nodoc/>
        public IEqualityComparer<WorkerId> Comparer => EqualityComparerInstance;

        /// <nodoc/>
        public int CompareTo([AllowNull] WorkerId other) => Value.CompareTo(other.Value);

        /// <inheritdoc />
        public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

        /// <inheritdoc />
        public bool Equals(WorkerId other) => Value == other.Value;

        /// <inheritdoc />
        public override int GetHashCode() => Value;
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
        public class CachingBuilder : CachingBuilder<PackedTable.StringId.EqualityComparer>
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
