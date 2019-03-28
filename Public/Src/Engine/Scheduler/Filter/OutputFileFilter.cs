// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips;
using BuildXL.Scheduler.Graph;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Filters pips by output file path.
    /// </summary>
    public sealed class OutputFileFilter : PathBasedFilter
    {
        /// <summary>
        /// Creates a new instance of <see cref="OutputFileFilter"/>.
        /// </summary>
        public OutputFileFilter(AbsolutePath path, string pathWildcard, MatchMode matchMode, bool pathFromMount)
            : base(path, pathWildcard, matchMode, pathFromMount)
        {
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            statistics.OutputFileFilterCount++;
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            OutputFileFilter outputFileFilter;
            return (outputFileFilter = pipFilter as OutputFileFilter) != null && base.CanonicallyEquals(outputFileFilter);
        }

        /// <inheritdoc/>
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(IPipFilterContext context, bool negate = false, IList<PipId> constrainingPips = null)
        {
            return ParallelProcessAllOutputs<FileOrDirectoryArtifact>(
                context,
                (pipId, localOutputs) => ForEachOutput(
                    localOutputs,
                    context,
                    pipId,
                    (localOutputs2, output) =>
                    {
                        if (output.IsFile)
                        {
                            if (PathMatches(output.Path, context.PathTable) ^ negate)
                            {
                                localOutputs2.Add(output);
                            }
                        }
                        else
                        {
                            // Directory can be non-output directory. See ForEachOutput.
                            if (output.DirectoryArtifact.IsOutputDirectory())
                            {
                                if (DirectoryPathMatches(output.Path, false, context.PathTable) ^ negate)
                                {
                                    localOutputs2.Add(output);
                                }
                            }
                        }
                    }),
                constrainingPips);
        }
    }
}
