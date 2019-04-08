// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Filters pips based on whether or not a pip has a tag in the multi tags.
    /// </summary>
    public class MultiTagsOrFilter : PipFilter
    {
        private readonly HashSet<StringId> m_tags;

        /// <summary>
        /// Tags in multi tags filter.
        /// </summary>
        public IEnumerable<StringId> Tags => m_tags;

        private readonly int m_cachedHashCode;

        /// <summary>
        /// Create an instance of <see cref="MultiTagsOrFilter"/>.
        /// </summary>
        public MultiTagsOrFilter(StringId tag1, StringId tag2)
        {
            m_tags = new HashSet<StringId> { tag1, tag2 };
            m_cachedHashCode = CalculateHashCode();
        }

        /// <summary>
        /// Create an instance of <see cref="MultiTagsOrFilter"/>.
        /// </summary>
        public MultiTagsOrFilter(params StringId[] tags)
        {
            m_tags = new HashSet<StringId>(tags);
            m_cachedHashCode = CalculateHashCode();
        }

        /// <summary>
        /// Create an instance of <see cref="MultiTagsOrFilter"/>.
        /// </summary>
        public MultiTagsOrFilter(IEnumerable<StringId> tags, StringId tag)
        {
            m_tags = new HashSet<StringId>(tags) { tag };
            m_cachedHashCode = CalculateHashCode();
        }

        /// <summary>
        /// Create an instance of <see cref="MultiTagsOrFilter"/>.
        /// </summary>
        public MultiTagsOrFilter(StringId tag, IEnumerable<StringId> tags)
        {
            m_tags = new HashSet<StringId> { tag };
            m_tags.UnionWith(tags);
            m_cachedHashCode = CalculateHashCode();
        }

        /// <summary>
        /// Create an instance of <see cref="MultiTagsOrFilter"/>.
        /// </summary>
        public MultiTagsOrFilter(IEnumerable<StringId> tags1, IEnumerable<StringId> tags2)
        {
            m_tags = new HashSet<StringId>(tags1);
            m_tags.UnionWith(tags2);
            m_cachedHashCode = CalculateHashCode();
        }

        private bool Matches(Pip pip)
        {
            if (pip.Tags.IsValid)
            {
                foreach (StringId tag in pip.Tags)
                {
                    if (m_tags.Contains(tag))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(IPipFilterContext context, bool negate = false, IList<PipId> constrainingPips = null)
        {
            return ParallelProcessAllOutputs<FileOrDirectoryArtifact>(
                context,
                (pipId, localOutputs) =>
                {
                    if (MayHaveOutputs(context, pipId))
                    {
                        Pip pip = context.HydratePip(pipId);
                        if (Matches(pip) ^ negate)
                        {
                            AddOutputs(pip, localOutputs);
                        }
                    }
                },
                constrainingPips);
        }

        /// <inheritdoc />
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            statistics.MultiTagsFilterCount++;
        }

        /// <inheritdoc />
        protected override int GetDerivedSpecificHashCode()
        {
            return m_cachedHashCode;
        }

        /// <inheritdoc />
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            MultiTagsOrFilter multiTagsFilter;
            return (multiTagsFilter = pipFilter as MultiTagsOrFilter) != null && m_tags.SetEquals(multiTagsFilter.m_tags);
        }

        private int CalculateHashCode()
        {
            return HashCodeHelper.Combine(m_tags.OrderBy(id => id.Value).ToArray(), id => id.GetHashCode());
        }

        /// <inheritdoc/>
        public override UnionFilterKind UnionFilterKind => UnionFilterKind.Tags;

        /// <inheritdoc/>
        public override PipFilter Union(IEnumerable<PipFilter> filters)
        {
            return UnionTagFilters(filters);
        }

        internal static PipFilter UnionTagFilters(IEnumerable<PipFilter> filters)
        {
            Contract.Assume(Contract.ForAll(filters, filter => filter is TagFilter || filter is MultiTagsOrFilter));
            return new MultiTagsOrFilter(filters.SelectMany(f => GetTags(f)).ToArray());
        }

        private static IEnumerable<StringId> GetTags(PipFilter filter)
        {
            if (filter is MultiTagsOrFilter multiFilter)
            {
                return multiFilter.Tags;
            }
            else
            {
                return new[] { ((TagFilter)filter).Tag };
            }
        }
    }
}
