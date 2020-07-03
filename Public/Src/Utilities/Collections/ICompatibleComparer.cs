// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Tag interface for an <see cref="IComparer{T}" /> that is compatible with another.
    /// Here 'compatible' means that an array sorted by <typeparamref name="TComparer" />
    /// is already sorted according to this comparer (this is possibly a one way relation).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1040:AvoidEmptyInterfaces")]
    public interface ICompatibleComparer<in TValue, TComparer> : IComparer<TValue>
        where TComparer : IComparer<TValue>
    {
    }
}
