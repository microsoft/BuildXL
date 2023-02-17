// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// The identifier of a cache within a cache hierarchy
    /// </summary>
    [EventData]
    public readonly struct CacheId : IEquatable<CacheId>
    {
        private readonly List<string> m_hierarchicalIds;

        /// <nodoc/>
        public CacheId(params string[] hierarchicalIds)
        {
            m_hierarchicalIds = new List<string>(hierarchicalIds);
        }

        /// <nodoc/>
        public CacheId(params CacheId[] cacheIds)
        {
            m_hierarchicalIds = new List<string>(cacheIds.SelectMany(cacheId => cacheId.HierarchicalIds));
        }

        /// <nodoc/>
        public static CacheId Invalid = new CacheId();

        /// <nodoc/>
        public bool IsValid => m_hierarchicalIds?.Count > 0;

        /// <nodoc/>
        public IReadOnlyCollection<string> HierarchicalIds => m_hierarchicalIds;

        /// <nodoc/>
        public int Depth => m_hierarchicalIds.Count;

        /// <summary>
        /// Returns a flattened representation of this cache id
        /// </summary>
        public override string ToString() => string.Join("_", m_hierarchicalIds);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is CacheId id && Equals(id);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCodeHelper.Combine(m_hierarchicalIds.Select(id => id.GetHashCode()).ToArray());

        /// <nodoc/>
        public bool Equals(CacheId other) => other != null && m_hierarchicalIds.SequenceEqual<string>(other.HierarchicalIds);

        /// <summary>
        /// Implicitly converts this cache id to a flattened string representation of it
        /// </summary>
        /// <remarks>
        /// There are many consumers that still expect a cache id of string type. This implicit operator is only simplifying the migration and
        /// could be removed in the future
        /// </remarks>
        public static implicit operator string(CacheId cacheId) => cacheId.IsValid? cacheId.ToString() : null;
    }
}
