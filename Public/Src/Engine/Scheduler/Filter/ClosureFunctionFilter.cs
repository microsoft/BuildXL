// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Base function filter for getting transitive closure.
    /// </summary>
    public abstract class ClosureFunctionFilter<TFilter> : PipFilter
        where TFilter : ClosureFunctionFilter<TFilter>
    {
        /// <summary>
        /// inner filter
        /// </summary>
        public readonly PipFilter Inner;

        private readonly int m_cachedHashCode;

        /// <summary>
        /// Gets the behavior when evaluating the closure
        /// </summary>
        public readonly ClosureMode ClosureMode;

        /// <summary>
        /// Class constructor
        /// </summary>
        protected ClosureFunctionFilter(PipFilter inner, ClosureMode closureMode = ClosureMode.TransitiveIncludingSelf)
        {
            Inner = inner;
            ClosureMode = closureMode;
            m_cachedHashCode = HashCodeHelper.Combine(Inner.GetHashCode(), GetType().GetHashCode(), (int)ClosureMode);
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            Inner.AddStatistics(ref statistics);
        }

        /// <inheritdoc/>
        protected override int GetDerivedSpecificHashCode()
        {
            return m_cachedHashCode;
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            return pipFilter is TFilter closureFilter && ReferenceEquals(Inner, closureFilter.Inner);
        }

        public override PipFilter Canonicalize(FilterCanonicalizer canonicalizer)
        {
            var canonInner = Inner.Canonicalize(canonicalizer);
            var canonFilter = CreateFilter(canonInner);

            // Verify that closure mode is preserved
            Contract.Assert(canonFilter.ClosureMode == ClosureMode);

            return canonicalizer.GetOrAdd(canonFilter);
        }

        /// <summary>
        /// Creates a new instance of the given filter type with the given inner filter
        /// </summary>
        protected abstract TFilter CreateFilter(PipFilter inner);

        /// <summary>
        /// Gets the neighbor (dependents or dependencies) pips of the current pip
        /// </summary>
        protected abstract IEnumerable<PipId> GetNeighborPips(IPipFilterContext context, PipId pip);

        private bool IncludesInnerMatches
        {
            get
            {
                switch (ClosureMode)
                {
                    case ClosureMode.TransitiveIncludingSelf:
                        return true;
                    case ClosureMode.DirectExcludingSelf:
                    default:
                        Contract.Assert(ClosureMode == ClosureMode.DirectExcludingSelf);
                        return false;
                }
            }
        }

        /// <inheritdoc/>
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(IPipFilterContext context, bool negate = false, IList<PipId> constrainingPips = null)
        {
            var outputs = Inner.FilterOutputs(context, negate: false);

            HashSet<PipId> innerPipProducers = new HashSet<PipId>();
            foreach (var innerOutput in outputs)
            {
                innerPipProducers.Add(context.GetProducer(innerOutput));
            }

            var producersAndNeighbors = GetClosureWithOutputs(context, innerPipProducers, GetNeighborPips, ClosureMode);

            if (IncludesInnerMatches)
            {
                // Add producers
                producersAndNeighbors.UnionWith(innerPipProducers);
            }
            else
            {
                // Exclude inner matchs
                producersAndNeighbors.ExceptWith(innerPipProducers);
            }

            // When not negated the set of pips to process is
            // the intersection of the constraining pips and the producers and
            // neighbors set.
            if (!negate)
            {
                if (constrainingPips == null)
                {
                    // No constraining pips, so only consider the producers and neighbors
                    constrainingPips = producersAndNeighbors.ToList();
                }
                else
                {
                    // Has constraining pips, so intersect with the producers and neighbors
                    producersAndNeighbors.IntersectWith(constrainingPips);
                    constrainingPips = producersAndNeighbors.ToList();
                }
            }

            return ParallelProcessAllOutputs<FileOrDirectoryArtifact>(
                context,
                action: (pipId, localOutputs) =>
                {
                    if (producersAndNeighbors.Contains(pipId) ^ negate)
                    {
                        ForEachOutput(
                            localOutputs,
                            context,
                            pipId,
                            (localOutputs2, output) => localOutputs2.Add(output));
                    }
                },
                pips: constrainingPips);
        }
    }

    /// <summary>
    /// The mode for evaluating closure filter
    /// </summary>
    public enum ClosureMode
    {
        /// <summary>
        /// Gets the transitive closure including the matching pips for the inner filter.
        /// </summary>
        TransitiveIncludingSelf,

        /// <summary>
        /// Gets only the direct neighbors (excludes the matching pips for the inner filter).
        /// NOTE: If pip is a matching neighbor of one of the matching pips for the inner filter
        /// it will be excluded.
        /// </summary>
        DirectExcludingSelf,
    }
}
