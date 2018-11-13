// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Allocation-free enumerable for a <see cref="IReadOnlyList{T}"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ReadOnlyListEnumerable<TList, T>
        where TList : IReadOnlyList<T>
    {
        private readonly TList m_array;

        /// <nodoc/>
        public ReadOnlyListEnumerable(TList array)
        {
            m_array = array;
        }

        /// <nodoc/>
        public ReadOnlyListEnumerator<TList, T> GetEnumerator()
        {
            return new ReadOnlyListEnumerator<TList, T>(m_array);
        }
    }

    /// <summary>
    /// Allocation-free enumerator for a <see cref="IReadOnlyList{T}"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct ReadOnlyListEnumerator<TList, T>
        where TList : IReadOnlyList<T>
    {
        private readonly TList m_array;
        private int m_index;

        /// <nodoc/>
        public ReadOnlyListEnumerator(TList array)
        {
            m_array = array;
            m_index = -1;
        }

        /// <nodoc/>
        public T Current => m_array[m_index];

        /// <nodoc/>
        public bool MoveNext()
        {
            if (m_index + 1 == (m_array?.Count ?? 0))
            {
                return false;
            }

            m_index++;
            return true;
        }
    }
}
