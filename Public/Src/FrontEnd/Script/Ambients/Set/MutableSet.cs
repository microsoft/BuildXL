// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Collections;
using InternalSet = System.Collections.Generic.HashSet<BuildXL.FrontEnd.Script.Ambients.Set.TaggedEntry>;

namespace BuildXL.FrontEnd.Script.Ambients.Set
{
    /// <summary>
    /// Wrapper around HashSet.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public sealed class MutableSet
    {
        private readonly InternalSet m_set;
        private int m_tag;

        /// <nodoc />
        private MutableSet(InternalSet set)
        {
            Contract.Requires(set != null);

            m_set = set;
        }

        /// <summary>
        /// Empty set.
        /// </summary>
        public static MutableSet CreateEmpty() => new MutableSet(new InternalSet());

        /// <nodoc />
        public int Count => m_set.Count;

        /// <nodoc />
        public MutableSet Add(EvaluationResult item)
        {
            m_set.Add(CreateNewEntry(item));
            return this;
        }

        /// <nodoc />
        public MutableSet AddRange(IReadOnlyList<EvaluationResult> items)
        {
            Contract.Requires(items != null);

            m_set.UnionWith(items.SelectArray(v => CreateNewEntry(v)));
            return this;
        }

        /// <nodoc />
        public MutableSet RemoveRange(IReadOnlyList<EvaluationResult> items)
        {
            Contract.Requires(items != null);

            foreach (var item in items.AsStructEnumerable())
            {
                m_set.Remove(new TaggedEntry(item));
            }

            return this;
        }

        /// <nodoc />
        public bool Contains(EvaluationResult value)
        {
            return m_set.Contains(new TaggedEntry(value));
        }

        /// <nodoc />
        public MutableSet Union(MutableSet otherSet)
        {
            Contract.Requires(otherSet != null);
            m_set.UnionWith(otherSet.m_set);
            return this;
        }

        /// <nodoc />
        public EvaluationResult[] ToArray()
        {
            var array = m_set.ToArray();

            Array.Sort(array);

            return array.SelectArray(v => v.Value);
        }

        private TaggedEntry CreateNewEntry(EvaluationResult item)
        {
            var tag = Interlocked.Increment(ref m_tag);
            return new TaggedEntry(item, tag);
        }
    }
}
