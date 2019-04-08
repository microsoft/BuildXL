// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Filters pips based on the value
    /// </summary>
    public class ValueFilter : PipFilter
    {
        private readonly FullSymbol m_value;
        private readonly bool m_valueTransitive;

        /// <summary>
        /// Creates a new instance of <see cref="ValueFilter"/>.
        /// </summary>
        public ValueFilter(FullSymbol value, bool valueTransitive = false)
        {
            m_value = value;
            m_valueTransitive = valueTransitive;
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            statistics.ValueFilterCount++;
        }

        /// <inheritdoc/>
        protected override int GetDerivedSpecificHashCode()
        {
            return m_value.GetHashCode();
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            ValueFilter valueFilter;
            return (valueFilter = pipFilter as ValueFilter) != null &&
                   m_value == valueFilter.m_value &&
                   m_valueTransitive == valueFilter.m_valueTransitive;
        }

        /// <inheritdoc/>
        public override IEnumerable<FullSymbol> GetValuesToResolve(bool negate = false)
        {
            return negate ? null : new[] { m_value };
        }

        /// <inheritdoc/>
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(
            IPipFilterContext context,
            bool negate = false,
            IList<PipId> constrainingPips = null)
        {
            var matchingValuePipIds = ParallelProcessAllOutputs<PipId>(
                context,
                (pipId, localPips) =>
                {
                    if (context.GetPipType(pipId) == PipType.Value)
                    {
                        ValuePip valuePip = (ValuePip)context.HydratePip(pipId);

                        // TODO: Consider not allowing matching of non-public values.
                        if (valuePip.Symbol == m_value ^ negate)
                        {
                            localPips.Add(pipId);
                        }
                    }
                });

            HashSet<PipId> dependenciesWithOutputs =
                m_valueTransitive
                    ? GetDependenciesWithOutputsBehindValueAndSealDirectoryPips(context, matchingValuePipIds)
                    : GetDependenciesWithOutputsForValuePips(context, matchingValuePipIds);

            return ParallelProcessAllOutputs<FileOrDirectoryArtifact>(
                context,
                (pipId, localOutputs) => AddOutputs(context, pipId, localOutputs),
                dependenciesWithOutputs.ToArray());
        }
    }
}
