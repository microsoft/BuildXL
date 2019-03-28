// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Values;
using InternalSet = System.Collections.Immutable.ImmutableHashSet<BuildXL.FrontEnd.Script.Ambients.Set.TaggedEntry>;

namespace BuildXL.FrontEnd.Script.Ambients.Set
{
    /// <summary>
    /// Ordered set, where the ordering is based on the insertion order.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public sealed class OrderedSet : IEnumerable<EvaluationResult>
    {
        private readonly InternalSet m_set;
        private readonly long m_maxTag;

        /// <summary>
        /// Empty set.
        /// </summary>
        public static readonly OrderedSet Empty = new OrderedSet(InternalSet.Empty, 0);

        /// <nodoc />
        internal OrderedSet(InternalSet set, long maxTag)
        {
            Contract.Requires(set != null);

            m_set = set;
            m_maxTag = maxTag;
        }

        /// <nodoc />
        public int Count => m_set.Count;

        /// <nodoc />
        public OrderedSet Add(EvaluationResult item)
        {
            return new OrderedSet(m_set.Add(new TaggedEntry(item, m_maxTag)), m_maxTag + 1);
        }

        /// <nodoc />
        public OrderedSet AddRange(IReadOnlyList<EvaluationResult> items)
        {
            Contract.Requires(items != null);

            // Special logic if the right hand side is empty or has just one element
            if (items.Count == 0)
            {
                return this;
            }

            if (items.Count == 1)
            {
                return Add(items[0]);
            }

            var taggedItems = new TaggedEntry[items.Count];
            long tag = m_maxTag;

            for (int i = 0; i < items.Count; ++i)
            {
                taggedItems[i] = new TaggedEntry(items[i], tag++);
            }

            return new OrderedSet(m_set.Union(taggedItems), tag);
        }

        /// <nodoc />
        public bool Contains(EvaluationResult item)
        {
            return m_set.Contains(new TaggedEntry(item));
        }

        /// <nodoc />
        public OrderedSet Remove(EvaluationResult item)
        {
            return new OrderedSet(m_set.Remove(new TaggedEntry(item)), m_maxTag);
        }

        /// <nodoc />
        public OrderedSet RemoveRange(IReadOnlyList<EvaluationResult> items)
        {
            Contract.Requires(items != null);
            return new OrderedSet(m_set.Except(items.Select(i => new TaggedEntry(i))), m_maxTag);
        }

        /// <nodoc />
        public bool IsSubsetOf(OrderedSet otherSet)
        {
            Contract.Requires(otherSet != null);
            return m_set.IsSubsetOf(otherSet.m_set);
        }

        /// <nodoc />
        public bool IsProperSubsetOf(OrderedSet otherSet)
        {
            Contract.Requires(otherSet != null);
            return m_set.IsProperSubsetOf(otherSet.m_set);
        }

        /// <nodoc />
        public bool IsSupersetOf(OrderedSet otherSet)
        {
            Contract.Requires(otherSet != null);
            return m_set.IsSupersetOf(otherSet.m_set);
        }

        /// <nodoc />
        public bool IsProperSupersetOf(OrderedSet otherSet)
        {
            Contract.Requires(otherSet != null);
            return m_set.IsProperSupersetOf(otherSet.m_set);
        }

        /// <nodoc />
        public OrderedSet Union(OrderedSet otherSet)
        {
            Contract.Requires(otherSet != null);

            var sortedOther = ToSortedArray(otherSet);
            var retaggedOther = new TaggedEntry[sortedOther.Length];
            long tag = m_maxTag;

            for (int i = 0; i < sortedOther.Length; ++i)
            {
                retaggedOther[i] = new TaggedEntry(sortedOther[i].Value, tag++);
            }

            return new OrderedSet(m_set.Union(retaggedOther), tag);
        }

        /// <nodoc />
        public OrderedSet Intersect(OrderedSet otherSet)
        {
            Contract.Requires(otherSet != null);
            return new OrderedSet(m_set.Intersect(otherSet.m_set), m_maxTag);
        }

        /// <nodoc />
        public OrderedSet Except(OrderedSet otherSet)
        {
            Contract.Requires(otherSet != null);
            return new OrderedSet(m_set.Except(otherSet.m_set), m_maxTag);
        }

        /// <nodoc />
        public EvaluationResult[] ToArray()
        {
            return ToEnumerable().ToArray();
        }

        private IEnumerable<EvaluationResult> ToEnumerable()
        {
            return ToSortedArray(this).Select(e => e.Value);
        }

        private static TaggedEntry[] ToSortedArray(OrderedSet set)
        {
            var sorted = set.m_set.ToArray();
            Array.Sort(sorted);
            return sorted;
        }

        /// <inheritdoc />
        public IEnumerator<EvaluationResult> GetEnumerator()
        {
            return ToEnumerable().GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
