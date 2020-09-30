// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Interface to tables which store multiple values per ID.
    /// </summary>
    public interface IMultiValueTable<TId, TValue>
        where TId : unmanaged, Id<TId>
        where TValue : unmanaged
    {
        /// <summary>
        /// Get or set the values at the given ID.
        /// </summary>
        /// <remarks>
        /// To find out how many values there are, just get the Length of the returned ReadOnlySpan.
        /// </remarks>
        ReadOnlySpan<TValue> this[TId id] { get; set; }

        /// <summary>
        /// Add this value to the end of the Table.
        /// </summary>
        TId Add(ReadOnlySpan<TValue> value);

        /// <summary>
        /// Enumerate the values (if any) at the given ID.
        /// </summary>
        IEnumerable<TValue> Enumerate(TId id);
    }
}
