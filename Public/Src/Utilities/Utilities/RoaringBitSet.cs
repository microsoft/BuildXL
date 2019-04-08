// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Bit set that can switch its underlying representation from a bit vector to a set depending on the scenario.
    /// </summary>
    /// <remarks>
    /// This data structure is designed specifically for spec-to-spec dependencies.
    /// In some cases it is more useful to keep all the upstream and downstream dependencies as a bit vector,
    /// but in some cases the set is already computed.
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public sealed class RoaringBitSet : IEnumerable<int>
    {
        [CanBeNull]
        private readonly ConcurrentBitArray m_bitArray;

        [CanBeNull]
        private HashSet<int> m_set;

        [CanBeNull]
        private HashSet<AbsolutePath> m_pathSet;

        /// <nodoc />
        public RoaringBitSet(int length)
        {
            Contract.Requires(length >= 0, "length >= 0");
            m_bitArray = new ConcurrentBitArray(length);
        }

        /// <nodoc />
        private RoaringBitSet(ConcurrentBitArray bitArray)
        {
            m_bitArray = bitArray;
        }

        /// <nodoc />
        private RoaringBitSet(HashSet<int> set)
        {
            m_set = set;
        }

        /// <nodoc />
        public static RoaringBitSet FromBitArray(ConcurrentBitArray bitArray)
        {
            Contract.Requires(bitArray != null, "bitArray != null");
            return new RoaringBitSet(bitArray);
        }

        /// <nodoc />
        public static RoaringBitSet FromSet(HashSet<int> set)
        {
            Contract.Requires(set != null, "set != null");
            return new RoaringBitSet(set);
        }

        /// <summary>
        /// Sets a value for a given index and returns true if this method actually changed the value.
        /// </summary>
        public bool Set(int index, bool value)
        {
            return m_bitArray.TrySet(index, value);
        }

        /// <summary>
        /// Gets or sets the bit at the given index
        /// </summary>
        public bool this[int index]
        {
            get
            {
                Contract.Assert(m_bitArray != null, "m_bitArray != null");
                return m_bitArray[index];
            }

            set
            {
                Contract.Assert(m_bitArray != null, "m_bitArray != null");
                m_bitArray[index] = value;
            }
        }

        /// <nodoc />
        [NotNull]
        public HashSet<int> MaterializedSet
        {
            get
            {
                Contract.Assert(m_set != null, "m_set is null. Did you forget to call MaterializeSetIfNeeded?");
                return m_set;
            }
        }

        /// <nodoc />
        [NotNull]
        public HashSet<AbsolutePath> MaterializedSetOfPaths
        {
            get
            {
                Contract.Assert(m_pathSet != null, "m_pathSet is null. Did you forget to call MaterializeSetIfNeeded?");
                return m_pathSet;
            }
        }

        /// <nodoc />
        public HashSet<int> MaterializeSetIfNeeded<TState>(TState state, Func<TState, int, AbsolutePath> indexResolver)
        {
            HashSet<AbsolutePath> pathSet;

            if (m_set == null)
            {
                // Need to materialize all two sets.
                pathSet = new HashSet<AbsolutePath>();
                var set = new HashSet<int>();
                foreach (int idx in this)
                {
                    pathSet.Add(indexResolver(state, idx));
                    set.Add(idx);
                }

                m_set = set;
                m_pathSet = pathSet;

                return m_set;
            }

            if (m_pathSet == null)
            {
                // need to materialize only m_path set
                pathSet = new HashSet<AbsolutePath>();
                foreach (var index in m_set)
                {
                    pathSet.Add(indexResolver(state, index));
                }

                m_pathSet = pathSet;
            }

            return m_set;
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Returns struct-based enumerator for the current bit set.
        /// </summary>
        public Enumerator GetEnumerator()
        {
            return m_bitArray != null ? new Enumerator(m_bitArray) : new Enumerator(m_set);
        }

        /// <summary>
        /// Struct-enumerator for the <see cref="RoaringBitSet"/>.
        /// </summary>
        public struct Enumerator : IEnumerator<int>
        {
            private HashSet<int>.Enumerator m_setEnumerator;
            private readonly ConcurrentBitArray m_bitSet;
            private int m_currentIndex;
            private int m_currentValue;

            /// <nodoc />
            public Enumerator(ConcurrentBitArray bitSet)
                : this()
            {
                m_bitSet = bitSet;
                m_currentIndex = 0;
            }

            /// <nodoc />
            public Enumerator(HashSet<int> set)
                : this()
            {
                m_setEnumerator = set.GetEnumerator();
            }

            /// <nodoc />
            public int Current => m_bitSet != null ? m_currentValue : m_setEnumerator.Current;

            /// <nodoc />
            public bool MoveNext()
            {
                if (m_bitSet != null)
                {
                    for (; m_currentIndex < m_bitSet.Length; m_currentIndex++)
                    {
                        if (m_bitSet[m_currentIndex])
                        {
                            m_currentValue = m_currentIndex;

                            // Incrementing the index to avoid infinite loop.
                            m_currentIndex++;
                            return true;
                        }
                    }

                    return false;
                }

                return m_setEnumerator.MoveNext();
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            /// <nodoc/>
            public void Reset()
            {
            }

            object IEnumerator.Current => Current;
        }
    }
}
