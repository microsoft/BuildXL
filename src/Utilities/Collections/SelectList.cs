// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;

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
        public TResult this[int index]
        {
            get
            {
                return m_selector(UnderlyingList[index], index);
            }
        }

        /// <inheritdoc />
        public int Count
        {
            get
            {
                return UnderlyingList.Count;
            }
        }

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
