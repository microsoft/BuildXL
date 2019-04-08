// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Class for canonicalizing <see cref="PipFilter" />.
    /// </summary>
    /// <remarks>This class works by memoizing the pip filter creation.</remarks>
    public sealed class FilterCanonicalizer
    {
        private readonly Dictionary<PipFilter, PipFilter> m_canonFilters = new Dictionary<PipFilter, PipFilter>(new FilterComparer());
        private readonly HashSet<PipFilter> m_reducedFilters = new HashSet<PipFilter>();

        /// <summary>
        /// Gets or adds a canonicalized <see cref="PipFilter"/>.
        /// </summary>
        public PipFilter GetOrAdd(PipFilter filter)
        {
            return m_canonFilters.GetOrAdd(filter, pipFilter => pipFilter);
        }

        /// <summary>
        /// Marks the binary filter as reduced
        /// </summary>
        public void MarkReduced(BinaryFilter filter)
        {
            m_reducedFilters.Add(filter);
        }

        /// <summary>
        /// Gets whether the binary filter is marked as reduced
        /// </summary>
        public bool IsReduced(BinaryFilter filter)
        {
            return m_reducedFilters.Contains(filter);
        }

        /// <summary>
        /// Tries get a canonicalized <see cref="PipFilter"/>.
        /// </summary>
        public bool TryGet(PipFilter filter, out PipFilter canonicalizedFilter)
        {
            return m_canonFilters.TryGetValue(filter, out canonicalizedFilter);
        }

        private class FilterComparer : IEqualityComparer<PipFilter>
        {
            public bool Equals(PipFilter x, PipFilter y)
            {
                return x.CanonicallyEquals(y);
            }

            public int GetHashCode(PipFilter f)
            {
                return f.GetHashCode();
            }
        }
    }
}
