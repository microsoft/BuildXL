// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// List which applies selector function on elements of underlying list when accessed
    /// </summary>
    public sealed class SelectList<T, TResult> : IReadOnlyList<TResult>
    {
        /// <summary>
        /// The underlying list
        /// </summary>
        public readonly IReadOnlyList<T> UnderlyingList;
        private readonly Func<T, int, TResult> m_selector;

        /// <nodoc />
        public SelectList(IReadOnlyList<T> underlyingList, Func<T, TResult> selector)
        {
            UnderlyingList = underlyingList;
            m_selector = (item, index) => selector(item);
        }

        /// <nodoc />
        public SelectList(IReadOnlyList<T> underlyingList, Func<T, int, TResult> selector)
        {
            UnderlyingList = underlyingList;
            m_selector = selector;
        }

        /// <inheritdoc />
        public TResult this[int index] => m_selector(UnderlyingList[index], index);

        /// <inheritdoc />
        public int Count => UnderlyingList.Count;

        /// <inheritdoc />
        public IEnumerator<TResult> GetEnumerator()
        {
            for (int i = 0; i < UnderlyingList.Count; i++)
            {
                yield return this[i];
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// List which applies selector function on elements of underlying list when accessed
    /// </summary>
    public sealed class SelectList<T, TResult, TState> : IReadOnlyList<TResult>
    {
        /// <summary>
        /// The underlying list
        /// </summary>
        public readonly IReadOnlyList<T> UnderlyingList;
        private readonly Func<T, int, TState, TResult> m_selector;
        
        [MaybeNull]
        private readonly TState m_state;

        /// <nodoc />
        public SelectList(IReadOnlyList<T> underlyingList, Func<T, int, TState, TResult> selector, TState state)
        {
            UnderlyingList = underlyingList;
            m_selector = selector;
            m_state = state;
        }

        /// <inheritdoc />
        public TResult this[int index] => m_selector(UnderlyingList[index], index, m_state!);

        /// <inheritdoc />
        public int Count => UnderlyingList.Count;

        /// <inheritdoc />
        public IEnumerator<TResult> GetEnumerator()
        {
            for (int i = 0; i < UnderlyingList.Count; i++)
            {
                yield return this[i];
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
