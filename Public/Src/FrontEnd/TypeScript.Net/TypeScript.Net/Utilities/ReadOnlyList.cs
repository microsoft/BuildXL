// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using JetBrains.Annotations;
using static BuildXL.Utilities.Collections.CollectionUtilities;

namespace TypeScript.Net
{
    /// <summary>
    /// Light-weight struct that wraps one element or <see cref="IReadOnlyList{T}"/> instance.
    /// </summary>
    public readonly struct ReadOnlyList<T> : IReadOnlyList<T> where T : class
    {
        private readonly T m_instance;

        [CanBeNull]
        private readonly IReadOnlyList<T> m_list;

        /// <nodoc />
        public ReadOnlyList(T instance)
            : this()
        {
            Contract.Requires(instance != null);
            m_instance = instance;
        }

        /// <nodoc />
        public ReadOnlyList([CanBeNull]IReadOnlyList<T> list)
            : this()
        {
            m_list = list;
        }

        /// <nodoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <nodoc />
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <nodoc />
        public ReadOnlyListEnumerator GetEnumerator()
        {
            return SingleItem ? new ReadOnlyListEnumerator(m_instance) : new ReadOnlyListEnumerator(m_list);
        }

        /// <nodoc />
        public bool SingleItem => m_instance != null;

        /// <nodoc />
        public int Count => SingleItem ? 1 : m_list?.Count ?? 0;

        /// <nodoc />
        public T this[int index] => SingleItem ? m_instance : m_list?[index];

        /// <nodoc />
        [CanBeNull]
        public T FirstOrDefault()
        {
            return SingleItem ? m_instance : FirstOrDefault(m_list);
        }

        /// <nodoc />
        [JetBrains.Annotations.NotNull]
        public T First()
        {
            var result = FirstOrDefault();
            if (result == default(T))
            {
                throw new InvalidOperationException("The list is empty");
            }

            return result;
        }

        /// <nodoc />
        public bool Any(Func<T, bool> predicate)
        {
            foreach (var e in this)
            {
                if (predicate(e))
                {
                    return true;
                }
            }

            return false;
        }

        /// <nodoc />
        public List<T> ToList()
        {
            var result = new List<T>(Count);

            // TODO: can we leverage Array.Copy here?
            // If this class will implement ICollection<T>, then list.AddRange will use Array.Copy
            // but with the cost of boxing allocation.
            // This implementation, on the other hand, does not allocate, but could be less optimal than one based on Array.Copy.
            foreach (var element in this)
            {
                result.Add(element);
            }

            return result;
        }

        private static T FirstOrDefault(IReadOnlyList<T> list)
        {
            return list != null && list.Count > 0 ? list[0] : default(T);
        }

        /// <nodoc />
        public struct ReadOnlyListEnumerator : IEnumerator<T>
        {
            private bool m_movedNext;
            private readonly bool m_singleItem;
            private readonly T m_item;
            private ReadOnlyListEnumerator<T> m_enumerator;

            /// <nodoc />
            public ReadOnlyListEnumerator(T item)
                : this()
            {
                m_item = item;
                m_singleItem = true;
                m_movedNext = false;
            }

            /// <nodoc />
            public ReadOnlyListEnumerator(IReadOnlyList<T> list)
                : this()
            {
                m_enumerator = new ReadOnlyListEnumerable<T>(list).GetEnumerator();
                m_singleItem = false;
                m_movedNext = false;
            }

            /// <nodoc />
            public void Dispose()
            {
            }

            /// <nodoc />
            public bool MoveNext()
            {
                if (m_singleItem)
                {
                    if (m_movedNext)
                    {
                        return false;
                    }

                    m_movedNext = true;
                    return true;
                }

                return m_enumerator.MoveNext();
            }

            /// <nodoc />
            public void Reset()
            {
            }

            /// <nodoc />
            public T Current => m_singleItem ? m_item : m_enumerator.Current;

            /// <nodoc />
            object IEnumerator.Current => Current;
        }
    }
}
