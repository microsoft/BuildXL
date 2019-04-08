// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Filters pips by a spec file.
    /// </summary>
    public sealed class SpecFileFilter : PathBasedFilter
    {
        private readonly bool m_valueTransitive;
        private readonly bool m_specDependencies;

        /// <summary>
        /// Creates a new instance of <see cref="SpecFileFilter"/>.
        /// </summary>
        public SpecFileFilter(AbsolutePath path, string pathWildcard, MatchMode matchMode, bool pathFromMount, bool valueTransitive, bool specDependencies)
            : base(path, pathWildcard, matchMode, pathFromMount)
        {
            m_valueTransitive = valueTransitive;
            m_specDependencies = specDependencies;
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            statistics.SpecFileFilterCount++;
        }

        /// <inheritdoc/>
        protected override int GetDerivedSpecificHashCode()
        {
            return HashCodeHelper.Combine(m_valueTransitive ? 1 : 0, m_specDependencies ? 1 : 0, base.GetDerivedSpecificHashCode());
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            SpecFileFilter specFileFilter;
            return (specFileFilter = pipFilter as SpecFileFilter) != null &&
                   m_valueTransitive == specFileFilter.m_valueTransitive &&
                   m_specDependencies == specFileFilter.m_specDependencies &&
                   base.CanonicallyEquals(specFileFilter);
        }

        /// <inheritdoc/>
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(IPipFilterContext context, bool negate = false, IList<PipId> constrainingPips = null)
        {
            var matchingSpecFilePipIds = ParallelProcessAllOutputs<PipId>(
                context,
                (pipId, localPips) =>
                {
                    if (context.GetPipType(pipId) == PipType.SpecFile)
                    {
                        SpecFilePip specFilePip = (SpecFilePip)context.HydratePip(pipId);
                        if (PathMatches(specFilePip.SpecFile.Path, context.PathTable) ^ negate)
                        {
                            localPips.Add(pipId);
                        }
                    }
                });

            if (m_specDependencies)
            {
                AddTransitiveSpecDependencies(context, matchingSpecFilePipIds);
            }

            HashSet<PipId> dependenciesWithOutputs =
                m_valueTransitive
                    ? GetDependenciesWithOutputsBehindValueAndSealDirectoryPips(context, matchingSpecFilePipIds)
                    : GetDependenciesWithOutputsForSpecFilePips(context, matchingSpecFilePipIds);

            if (constrainingPips != null)
            {
                dependenciesWithOutputs.IntersectWith(constrainingPips);
            }

            return ParallelProcessAllOutputs<FileOrDirectoryArtifact>(
                context,
                (pipId, localOutputs) => AddOutputs(context, pipId, localOutputs),
                dependenciesWithOutputs.ToArray());
        }

        public override IEnumerable<AbsolutePath> GetSpecRootsToResolve(bool negate = false)
        {
            if (negate)
            {
                return null;
            }

            // Paths may be defined as Mount[MountName]. These cannot be utilized for evaluation filtering since a dummy
            // mount path expander can be used for their construction
            if (PathFromMount)
            {
                return null;
            }

            // SpecDependencies (specref) does a mini traversal. When the spec filter using this option we cannot
            // support partial evaluation
            if (m_specDependencies)
            {
                return null;
            }

            AbsolutePath path = GetSpecRootToResolve();
            if (path.IsValid)
            {
                return new[] { path };
            }

            return null;
        }
    }
}
