// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
