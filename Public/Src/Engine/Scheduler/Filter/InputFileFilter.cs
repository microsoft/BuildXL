// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Filters pips by input file path
    /// </summary>
    public sealed class InputFileFilter : PathBasedFilter
    {
        /// <summary>
        /// Creates a new instance of <see cref="InputFileFilter"/>.
        /// </summary>
        public InputFileFilter(AbsolutePath path, string pathWildcard, MatchMode matchMode, bool pathFromMount)
            : base(path, pathWildcard, matchMode, pathFromMount)
        {
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
            statistics.InputFileFilterCount++;
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            InputFileFilter inputFileFilter;
            return (inputFileFilter = pipFilter as InputFileFilter) != null && base.CanonicallyEquals(inputFileFilter);
        }

        /// <inheritdoc/>
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(IPipFilterContext context, bool negate = false, IList<PipId> constrainingPips = null)
        {
            // First we collect all matching seal directories
            HashSet<DirectoryArtifact> directories = ParallelProcessAllOutputs<DirectoryArtifact>(
                context,
                (pipId, localDirectories) =>
                {
                    if (context.GetPipType(pipId) == PipType.SealDirectory)
                    {
                        SealDirectory sd = (SealDirectory)context.HydratePip(pipId);
                        foreach (var item in sd.Contents)
                        {
                            if (PathMatches(item.Path, context.PathTable))
                            {
                                localDirectories.Add(sd.Directory);
                                break;
                            }
                        }

                        switch (sd.Kind)
                        {
                            case SealDirectoryKind.SourceAllDirectories:
                            case SealDirectoryKind.Opaque:
                            case SealDirectoryKind.SharedOpaque:
                                if (DirectoryPathMatches(sd.DirectoryRoot, false, context.PathTable))
                                {
                                    localDirectories.Add(sd.Directory);
                                }
                                break;
                            case SealDirectoryKind.SourceTopDirectoryOnly:
                                if (DirectoryPathMatches(sd.DirectoryRoot, true, context.PathTable))
                                {
                                    localDirectories.Add(sd.Directory);
                                }
                                break;
                        }
                    }
                });

            // Now look at all pips, checking if their input match one of the files or matching DirectoryArtifacts
            return ParallelProcessAllOutputs<FileOrDirectoryArtifact>(
                context,
                (pipId, localOutputs) =>
                {
                    switch (context.GetPipType(pipId))
                    {
                        case PipType.CopyFile:
                            CopyFile cf = (CopyFile)context.HydratePip(pipId);
                            if (PathMatches(cf.Source, context.PathTable) ^ negate)
                            {
                                localOutputs.Add(FileOrDirectoryArtifact.Create(cf.Destination));
                            }

                            break;

                        case PipType.Process:
                            Process proc = (Process)context.HydratePip(pipId);
                            bool processMatches =
                                PathMatches(proc.Executable, context.PathTable) ||
                                MatchesDependencies(context, proc) ||
                                MatchesDirectoryDependencies(proc, directories);
                            if (processMatches ^ negate)
                            {
                                // TODO: If only directory dependencies matched, only include outputs from those directories
                                foreach (var output in proc.FileOutputs)
                                {
                                    localOutputs.Add(FileOrDirectoryArtifact.Create(output.ToFileArtifact()));
                                }

                                foreach (var output in proc.DirectoryOutputs)
                                {
                                    localOutputs.Add(FileOrDirectoryArtifact.Create(output));
                                }
                            }

                            break;
                    }
                },
                constrainingPips);
        }

        private bool MatchesDependencies(IPipFilterContext context, Process proc)
        {
            foreach (var item in proc.Dependencies)
            {
                if (PathMatches(item.Path, context.PathTable))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesDirectoryDependencies(Process proc, HashSet<DirectoryArtifact> directories)
        {
            foreach (var item in proc.DirectoryDependencies)
            {
                if (directories.Contains(item))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
