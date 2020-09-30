// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// An ITable defines an ID space for its elements.
    /// </summary>
    /// <remarks>
    /// Derived ITable interfaces allow getting at one (ISingleValueTable)
    /// or multiple (IMultipleValueTable) values per ID.
    /// </remarks>
    public interface ITable<TId>
        where TId : unmanaged, Id<TId>
    {
        /// <summary>
        /// The IDs stored in this Table.
        /// </summary>
        IEnumerable<TId> Ids { get; }

        /// <summary>
        /// The number of IDs currently stored in the Table.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Is this ID valid in this table?
        /// </summary>
        bool IsValid(TId id);

        /// <summary>
        /// Check that the ID is valid, throwing an exception if not.
        /// </summary>
        void CheckValid(TId id);

        /// <summary>
        /// Save the contents of this table in the given directory with the given filename.
        /// </summary>
        void SaveToFile(string directory, string name);

        /// <summary>
        /// Load the contents of this table from the given directory with the given filename.
        /// </summary>
        /// <remarks>
        /// Any existing contents of this table will be discarded before loading.
        /// </remarks>
        void LoadFromFile(string directory, string name);

        /// <summary>
        /// The base table for this table, if any.
        /// </summary>
        /// <remarks>
        /// The base table, if it exists, defines the ID space for this table; this table is
        /// conceptually a "derived column" (or additional relation) over the IDs of the base
        /// table.
        /// </remarks>
        ITable<TId> BaseTableOpt { get; }

        /// <summary>
        /// Fill this table to have as many (default-value) entries as BaseTableOpt.
        /// </summary>
        /// <remarks>
        /// It is an error to call this on a table with no BaseTableOpt set.
        /// 
        /// This method is used when a base table gets populated before a derived table,
        /// and the derived table may get populated in random order, so needs to be
        /// the same size as the base table.
        /// </remarks>
        void FillToBaseTableCount();
    }
}
