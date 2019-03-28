// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents a mapping of <see cref="HierarchicalNameId" /> to values, such that a particular hierarchy of names can
    /// be queried bottom-up for mappings.
    /// </summary>
    /// <remarks>
    /// Names added to the dictionary are flagged with <see cref="HierarchicalNameTable.SetFlags" />
    /// as a first-level filter. Since that is global to the table, it admits false positives.
    /// As a second-level filter, this implementation tracks those names actually added.
    /// This implementation is thread-safe.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public sealed class FlaggedHierarchicalNameDictionary<TValue>
    {
        private readonly HierarchicalNameTable m_nameTable;
        private readonly HierarchicalNameTable.NameFlags m_memberFlags;
        private readonly ConcurrentDictionary<HierarchicalNameId, TValue> m_map = new ConcurrentDictionary<HierarchicalNameId, TValue>();

        /// <summary>
        /// Creates a name dictionary. Names added to the dictionary will have all of <paramref name="flags" /> set in the backing name table.
        /// </summary>
        public FlaggedHierarchicalNameDictionary(HierarchicalNameTable nameTable, HierarchicalNameTable.NameFlags flags)
        {
            Contract.Requires(nameTable != null);
            m_nameTable = nameTable;
            m_memberFlags = flags;
        }

        /// <summary>
        /// Gets the value for the specified name key (which must be present in the dictionary).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1043:UseIntegralOrStringArgumentForIndexers")]
        public TValue this[HierarchicalNameId name] => m_map[name];

        /// <summary>
        /// Adds a mapping from the given name to the given value. If false is returned, a mapping already existed (and the existing mapping was not updated).
        /// </summary>
        public bool TryAdd(HierarchicalNameId name, TValue value)
        {
            Contract.Requires(name.IsValid);

            if (m_map.TryAdd(name, value))
            {
                // TODO: Since we can atomically know the set of previous flags, it would be much nicer if we avoided
                //       using m_added at all in the common case (i.e., only a single scheduler instance is using flags)
                Analysis.IgnoreResult(m_nameTable.SetFlags(name, m_memberFlags));
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Adds a mapping from the given name to the given value, returning either the existing value or a newly added one.
        /// The semantics are the same as <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,System.Func{TKey,TValue})"/>
        /// </summary>
        public TValue GetOrAdd(HierarchicalNameId name, Func<HierarchicalNameId, TValue> valueFactory)
        {
            Contract.Requires(name.IsValid);

            TValue effectiveValue = m_map.GetOrAdd(name, valueFactory);

            // TODO: Since we can atomically know the set of previous flags, it would be much nicer if we avoided
            //       using m_added at all in the common case (i.e., only a single scheduler instance is using flags)
            Analysis.IgnoreResult(m_nameTable.SetFlags(name, m_memberFlags));
            return effectiveValue;
        }

        /// <summary>
        /// Visits each name in the hierarchy from <paramref name="start"/> (bottom-up), and returns each mapping found.
        /// </summary>
        public BottomUpMappingEnumerator EnumerateMappingsInHierarchyBottomUp(HierarchicalNameId start)
        {
            return new BottomUpMappingEnumerator(m_nameTable.EnumerateHierarchyBottomUp(start, flagsFilter: m_memberFlags), m_map);
        }

        /// <summary>
        /// Visits each name in the hierarchy from <paramref name="start"/> (bottom-up), and returns the first mapping found.
        /// </summary>
        public bool TryGetFirstMapping(HierarchicalNameId start, out KeyValuePair<HierarchicalNameId, TValue> mapping)
        {
            foreach (var currentMapping in EnumerateMappingsInHierarchyBottomUp(start))
            {
                mapping = currentMapping;
                return true;
            }

            mapping = default(KeyValuePair<HierarchicalNameId, TValue>);
            return false;
        }

        /// <summary>
        /// Enumerator for enumerating mappings bottom up
        /// </summary>
        /// <remarks>
        /// This is a mutable struct, which is a precarious matter. The goal is to avoid allocations
        /// for the <c>foreach (T item in array)</c> construct, though that requires the compiler to
        /// see this type rather than <see cref="IEnumerator{T}"/> (otherwise the enumerator is boxed anyway).
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
        public struct BottomUpMappingEnumerator : IEnumerator<KeyValuePair<HierarchicalNameId, TValue>>, IEnumerable<KeyValuePair<HierarchicalNameId, TValue>>
        {
            private HierarchicalNameTable.ContainerEnumerator m_flagsEnumerator;
            private readonly ConcurrentDictionary<HierarchicalNameId, TValue> m_map;

            internal BottomUpMappingEnumerator(HierarchicalNameTable.ContainerEnumerator flagsEnumerator, ConcurrentDictionary<HierarchicalNameId, TValue> map)
            {
                m_flagsEnumerator = flagsEnumerator;
                m_map = map;
            }

            /// <inheritdoc/>
            public KeyValuePair<HierarchicalNameId, TValue> Current
            {
                get
                {
                    var currentKey = m_flagsEnumerator.Current;
                    return !currentKey.IsValid
                        ? default(KeyValuePair<HierarchicalNameId, TValue>)
                        : new KeyValuePair<HierarchicalNameId, TValue>(currentKey, m_map[currentKey]);
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            /// <inheritdoc/>
            object System.Collections.IEnumerator.Current => Current;

            /// <inheritdoc/>
            public bool MoveNext()
            {
                while (TryInnerMoveNext())
                {
                    if (m_map.ContainsKey(m_flagsEnumerator.Current))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool TryInnerMoveNext()
            {
                var flagsEnumerator = m_flagsEnumerator;
                var result = flagsEnumerator.MoveNext();
                m_flagsEnumerator = flagsEnumerator;
                return result;
            }

            /// <inheritdoc/>
            public void Reset()
            {
                m_flagsEnumerator.Reset();
            }

            /// <summary>
            /// Gets the enumerator
            /// </summary>
            public BottomUpMappingEnumerator GetEnumerator()
            {
                return this;
            }

            IEnumerator<KeyValuePair<HierarchicalNameId, TValue>> IEnumerable<KeyValuePair<HierarchicalNameId, TValue>>.GetEnumerator()
            {
                return this;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this;
            }
        }
    }
}
