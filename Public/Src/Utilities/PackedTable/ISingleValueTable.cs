// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Interface to tables which store one value per ID.
    /// </summary>
    public interface  ISingleValueTable<TId, TValue>
        where TId : unmanaged, Id<TId>
        where TValue : unmanaged
    {
        /// <summary>
        /// Get or set the value at the given ID.
        /// </summary>
        TValue this[TId id] { get; set; }

        /// <summary>
        /// Add this value to the end of the Table.
        /// </summary>
        TId Add(TValue value);
    }
}
