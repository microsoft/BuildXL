// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Ambients.Set;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Collections;

namespace BuildXL.FrontEnd.Script.Ambients.Map
{
    /// <summary>
    /// Ordered map, where the ordering is based on the insertion order of mappings.
    /// </summary>
    /// <remarks>
    /// If one adds [kvp(k1, v1), kvp(k2, v2), kvp(k1, v3)], then the ordered map will be [kvp(k2, v2), kvp(k1, v3)].
    /// If one adds [kvp(k1, v1), kvp(k2, v2), kvp(k1, v1)], then the ordered map will be [kvp(k1, v1), kvp(k2, v2)].
    /// Note that the last kvp(k1, v1) in the latter scenario is considered as the same mapping as the first one.
    /// TODO: In the future we can consider different semantics that fit our needs.
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public sealed class OrderedMap : IEnumerable<KeyValuePair<EvaluationResult, EvaluationResult>>
    {
        private static readonly KeyValueComparer s_keyValueComparer = new KeyValueComparer();

        /// <summary>
        /// Empty map.
        /// </summary>
        public static readonly OrderedMap Empty = new OrderedMap(ImmutableDictionary<EvaluationResult, TaggedEntry>.Empty, 0);

        /// <summary>
        /// Empty map with case sensitive keys comparison.
        /// </summary>
        public static readonly OrderedMap EmptyCaseInsensitive = new OrderedMap(ImmutableDictionary.Create<EvaluationResult, TaggedEntry>(IgnoreCaseStringKeyComparer.Instance), 0);

        private readonly ImmutableDictionary<EvaluationResult, TaggedEntry> m_map;
        private readonly long m_maxTag;

        /// <nodoc />
        internal OrderedMap(ImmutableDictionary<EvaluationResult, TaggedEntry> map, long maxTag)
        {
            Contract.Requires(map != null);

            m_map = map;
            m_maxTag = maxTag;
        }

        /// <nodoc />
        public int Count => m_map.Count;

        /// <nodoc />
        public OrderedMap Add(EvaluationResult key, EvaluationResult value)
        {
            return new OrderedMap(m_map.SetItem(key, new TaggedEntry(value, m_maxTag)), m_maxTag + 1);
        }

        /// <nodoc />
        public OrderedMap AddRange(IReadOnlyList<KeyValuePair<EvaluationResult, EvaluationResult>> mappings)
        {
            Contract.Requires(mappings != null);

            var taggedEntries = new KeyValuePair<EvaluationResult, TaggedEntry>[mappings.Count];
            long tag = m_maxTag;

            for (int i = 0; i < mappings.Count; ++i)
            {
                taggedEntries[i] = new KeyValuePair<EvaluationResult, TaggedEntry>(mappings[i].Key, new TaggedEntry(mappings[i].Value, tag++));
            }

            return new OrderedMap(m_map.SetItems(taggedEntries), tag);
        }

        /// <nodoc />
        public bool ContainsKey(EvaluationResult key)
        {
            return m_map.ContainsKey(key);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1007:UseGenericsWhereAppropriate")]
        public bool TryGetValue(EvaluationResult key, out EvaluationResult value)
        {
            value = default(EvaluationResult);

            if (!m_map.TryGetValue(key, out TaggedEntry entry))
            {
                return false;
            }

            value = entry.Value;
            return true;
        }

        /// <summary>
        /// Gets the value for a given key.
        /// </summary>
        public EvaluationResult GetValue(EvaluationResult key)
        {
            Contract.Requires(ContainsKey(key));
            TryGetValue(key, out var result);
            return result;
        }

        /// <nodoc />
        public OrderedMap Remove(EvaluationResult key)
        {
            Contract.Requires(key.Value != null);
            return new OrderedMap(m_map.Remove(key), m_maxTag);
        }

        /// <nodoc />
        public OrderedMap RemoveRange(IReadOnlyList<EvaluationResult> keys)
        {
            Contract.Requires(keys != null);
            Contract.RequiresForAll(keys, k => k.Value != null);
            return new OrderedMap(m_map.RemoveRange(keys), m_maxTag);
        }

        /// <nodoc />
        public KeyValuePair<EvaluationResult, EvaluationResult>[] ToArray()
        {
            var sorted = ToSortedArray(this);

            KeyValuePair<EvaluationResult, EvaluationResult>[] result = new KeyValuePair<EvaluationResult, EvaluationResult>[Count];
            for (int i = 0; i < Count; i++)
            {
                result[i] = new KeyValuePair<EvaluationResult, EvaluationResult>(sorted[i].Key, sorted[i].Value.Value);
            }

            return result;
        }

        /// <nodoc />
        public EvaluationResult[] Keys()
        {
            return ToSortedArray(this).SelectArray(kvp => kvp.Key);
        }

        /// <nodoc />
        public EvaluationResult[] Values()
        {
            return ToSortedArray(this).SelectArray(kvp => kvp.Value.Value);
        }

        private IEnumerable<KeyValuePair<EvaluationResult, EvaluationResult>> ToEnumerable()
        {
            return ToArray();
        }

        private static KeyValuePair<EvaluationResult, TaggedEntry>[] ToSortedArray(OrderedMap map)
        {
            KeyValuePair<EvaluationResult, TaggedEntry>[] sorted = map.m_map.ToArray();
            Array.Sort(sorted, s_keyValueComparer);
            return sorted;
        }

        private sealed class KeyValueComparer : IComparer<KeyValuePair<EvaluationResult, TaggedEntry>>
        {
            /// <inheritdoc />
            public int Compare(KeyValuePair<EvaluationResult, TaggedEntry> x, KeyValuePair<EvaluationResult, TaggedEntry> y)
            {
                return x.Value.Tag.CompareTo(y.Value.Tag);
            }
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<EvaluationResult, EvaluationResult>> GetEnumerator()
        {
            return ToEnumerable().GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal sealed class IgnoreCaseStringKeyComparer : IEqualityComparer<EvaluationResult>
    {
        public static IgnoreCaseStringKeyComparer Instance { get; } = new IgnoreCaseStringKeyComparer();

        /// <inheritdoc />
        bool IEqualityComparer<EvaluationResult>.Equals(EvaluationResult x, EvaluationResult y)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(x.Value, y.Value);
        }

        /// <inheritdoc />
        public int GetHashCode(EvaluationResult obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Value);
        }
    }
}
