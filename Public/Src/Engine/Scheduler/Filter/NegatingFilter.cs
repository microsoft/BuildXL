// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Negating filter.
    /// </summary>
    public class NegatingFilter : PipFilter
    {
        /// <summary>
        /// Inner filter
        /// </summary>
        public readonly PipFilter Inner;

        private readonly int m_cachedHashCode;

        /// <summary>
        /// Creates a new instance of <see cref="NegatingFilter"/>.
        /// </summary>
        public NegatingFilter(PipFilter inner)
        {
            Inner = inner;
            m_cachedHashCode = ~Inner.GetHashCode();
        }

        /// <inheritDoc/>
        public override PipFilter Negate()
        {
            return Inner;
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            statistics.NegatingFilterCount++;
            Inner.AddStatistics(ref statistics);
        }

        /// <inheritdoc/>
        public override IEnumerable<FullSymbol> GetValuesToResolve(bool negate = false)
        {
            return Inner.GetValuesToResolve(!negate);
        }

        /// <inheritdoc/>
        public override IEnumerable<AbsolutePath> GetSpecRootsToResolve(bool negate = false)
        {
            return Inner.GetSpecRootsToResolve(!negate);
        }

        /// <inheritdoc/>
        public override IEnumerable<StringId> GetModulesToResolve(bool negate = false)
        {
            return Inner.GetModulesToResolve(!negate);
        }

        /// <inheritdoc/>
        protected override int GetDerivedSpecificHashCode()
        {
            return m_cachedHashCode;
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            NegatingFilter negatingFilter;
            return (negatingFilter = pipFilter as NegatingFilter) != null && ReferenceEquals(Inner, negatingFilter.Inner);
        }

        /// <inheritdoc/>
        public override PipFilter Canonicalize(FilterCanonicalizer canonicalizer)
        {
            var canonInner = Inner.Canonicalize(canonicalizer);
            return canonicalizer.GetOrAdd(new NegatingFilter(canonInner));
        }

        /// <inheritdoc/>
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(IPipFilterContext context, bool negate = false, IList<PipId> constrainingPips = null)
        {
            return Inner.FilterOutputs(context, !negate, constrainingPips);
        }
    }
}
