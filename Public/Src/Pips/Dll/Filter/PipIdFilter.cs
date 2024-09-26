// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Filter
{
    /// <summary>
    /// Filters pips by pip id.
    /// </summary>
    public class PipIdFilter : PipFilter
    {
        private readonly long m_semiStableHash;

        /// <summary>
        /// Creates a new instance of <see cref="PipIdFilter"/>.
        /// </summary>
        public PipIdFilter(long value)
        {
            m_semiStableHash = value;
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            statistics.PipIdFilterCount++;
        }

        /// <inheritdoc/>
        protected override int GetDerivedSpecificHashCode()
        {
            return m_semiStableHash.GetHashCode();
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            PipIdFilter pipIdFilter;
            return (pipIdFilter = pipFilter as PipIdFilter) != null && m_semiStableHash == pipIdFilter.m_semiStableHash;
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
                    // Only allow this filter to match on a few specific pip types. This prevents some unintended
                    // consequences like negated pip id filters matching all sealed directory pips.
                    //
                    // Note: this does not completely eliminate sealed directory pips from slipping into filtering since
                    // other filter groups may end up including them. This special case primarily guards against unintended
                    // pip inclusion via sealed directory pips in simple filters like ~(id='pip12345'), which could end
                    // up including pip12345 anyway since that filter would still match on an opaque directory produced
                    // by that pip.
                    var pipType = context.GetPipType(pipId);
                    if (pipType == Operations.PipType.Process || 
                        pipType == Operations.PipType.WriteFile || 
                        pipType == Operations.PipType.CopyFile)
                    {
                        if ((context.GetSemiStableHash(pipId) == m_semiStableHash) ^ negate)
                        {
                            AddOutputs(context, pipId, localOutputs);
                        }
                    }
                },
                constrainingPips);
        }
    }
}
