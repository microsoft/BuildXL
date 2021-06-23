// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Filter
{
    /// <summary>
    /// Filters pips based on whether a pip has or does not have a tag. Tag matching is case sensitive
    /// </summary>
    public class TagFilter : PipFilter
    {
        /// <summary>
        /// The tag.
        /// </summary>
        public readonly StringId Tag;

        /// <summary>
        /// Ensures pip triggers on a worker for testing purposes.
        /// </summary>
        public const string TriggerWorkerConnectionTimeout = "buildxl.internal:triggerWorkerConnectionTimeout";

        /// <summary>
        /// Ensures pip triggers on a worker for testing purposes.
        /// </summary>
        public const string TriggerWorkerProcessStartFailed = "buildxl.internal:triggerProcessStartFailed";

        /// <summary>
        /// Ensures pip triggers on a worker for testing purposes.
        /// </summary>
        public const string TriggerWorkerRemotePipTimeout = "buildxl.internal:triggerWorkerRemotePipTimeout";

        /// <summary>
        /// Ensures pip is dispatched to a remote worker for testing purposes.
        /// </summary>
        public const string RunPipRemotely = "buildxl.internal:runRemotely";

        /// <summary>
        /// Creates a new instance of <see cref="TagFilter"/>
        /// </summary>
        public TagFilter(StringId tag)
        {
            Tag = tag;
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            statistics.TagFilterCount++;
        }

        /// <inheritdoc/>
        protected override int GetDerivedSpecificHashCode()
        {
            return Tag.GetHashCode();
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            TagFilter tagFilter;
            return (tagFilter = pipFilter as TagFilter) != null && Tag == tagFilter.Tag;
        }

        /// <inheritdoc/>
        public override PipFilter Canonicalize(FilterCanonicalizer canonicalizer)
        {
            return canonicalizer.GetOrAdd(this);
        }

        /// <inheritdoc/>
        public override UnionFilterKind UnionFilterKind => UnionFilterKind.Tags;

        /// <inheritdoc/>
        public override PipFilter Union(IEnumerable<PipFilter> filters)
        {
            return MultiTagsOrFilter.UnionTagFilters(filters);
        }

        private bool Matches(Pip pip)
        {
            if (pip.Tags.IsValid)
            {
                foreach (StringId tag in pip.Tags)
                {
                    if (tag == Tag)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(
            IPipFilterContext context,
            bool negate = false,
            IList<PipId> constrainingPips = null)
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
    }
}
