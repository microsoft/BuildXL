// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Filter
{
    /// <summary>
    /// Filters Process pips by pip id; other pip types do not match.
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
        internal override string PipEvaluationFallbackReason => null;

        /// <inheritdoc/>
        internal override bool PipMatches(Pip pip, PathTable pathTable)
        {
            return pip.PipType == PipType.Process && pip.SemiStableHash == m_semiStableHash;
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
                    var pipType = context.GetPipType(pipId);
                    if (pipType == PipType.SealDirectory)
                    {
                        // Evaluate IDs against the producing Process, not its generated Opaque/SharedOpaque seal.
                        // Otherwise negation can match the seal's different ID and select the excluded Process's
                        // directory output. Static/source seals and composite seals are not skipped here.
                        if (context.GetSealDirectoryKind(pipId).IsDynamicKind() &&
                            !context.IsSealDirectoryComposite(pipId))
                        {
                            return;
                        }
                    }

                    bool matches = pipType == PipType.Process && context.GetSemiStableHash(pipId) == m_semiStableHash;
                    if (matches ^ negate)
                    {
                        AddOutputs(context, pipId, localOutputs);
                    }
                },
                constrainingPips);
        }
    }
}
