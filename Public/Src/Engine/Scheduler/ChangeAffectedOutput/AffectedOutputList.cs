using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Storage.InputChange;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.ChangeAffectedOutput
{
    /// <summary>
    /// Class representing source change affected output list.
    /// </summary>
    public sealed class AffectedOutputList
    {

        /// <summary>
        /// Output file contents which affected by the source change of this build. 
        /// </summary>
        /// <remarks>
        /// This list should be initialized by the input change list provided from the InputChanges configuration option.
        /// </remarks>
        private readonly ConcurrentBigSet<FileArtifact> m_sourceChangeAffectedOutputFiles = new ConcurrentBigSet<FileArtifact>();

        /// <summary>
        /// Output file contents which affected by the source change of this build. 
        /// </summary>
        private readonly ConcurrentBigSet<DirectoryArtifact> m_sourceChangeAffectedOutputDirectories = new ConcurrentBigSet<DirectoryArtifact>();

        private PathTable m_pathTable;

        /// <nodoc />
        public AffectedOutputList(PathTable pathTable)
        {
            m_pathTable = pathTable;
        }

        /// <summary>
        /// Initial affected output list with the external source change list
        /// </summary>
        public bool InitialAffectedOutputList(InputChangeList inputChangeList, PathTable pathTable)
        {
            var allInputsAreAffected = false;
            // The Algorithm can't deal with removal currently, so disable change-based code coverage and have to run full code coverage if has removal. 
            if (!inputChangeList.ChangedPaths.Any(p => p.PathChanges == PathChanges.Removed))
            {
                foreach (var changePath in inputChangeList.ChangedPaths)
                {
                    switch (changePath.PathChanges)
                    {
                        case PathChanges.DataOrMetadataChanged:
                        case PathChanges.NewlyPresentAsFile:
                            m_sourceChangeAffectedOutputFiles.GetOrAdd(new FileArtifact(AbsolutePath.Create(pathTable, changePath.Path)));
                            break;
                    }

                }
            }
            else
            {
                allInputsAreAffected = true;
            }

            return allInputsAreAffected;
        }

        /// <nodoc />
        public IReadOnlyCollection<FileArtifact> GetSourceChangeAffectedOutputs()
        {
            return m_sourceChangeAffectedOutputFiles.UnsafeGetList();
        }

        /// <nodoc />
        public IReadOnlyCollection<DirectoryArtifact> GetSourceChangeAffectedOutputDirectories()
        {
            return m_sourceChangeAffectedOutputDirectories.UnsafeGetList();
        }

        /// <summary>
        /// Compute the intersection of the pip's dependencies and global affected SourceChangeAffectedOutputs of the build
        /// </summary>
        public IReadOnlyCollection<FileArtifact> GetChangeAffectedInputs(Process process)
        {
            var changeAffectedInputs = new HashSet<FileArtifact>();

            changeAffectedInputs.AddRange(GetSourceChangeAffectedOutputs().Intersect(process.Dependencies));

            foreach (var file in GetSourceChangeAffectedOutputs())
            {
                foreach (var directory in process.DirectoryDependencies)
                {
                    if (file.Path.IsWithin(m_pathTable, directory.Path))
                    {
                        changeAffectedInputs.Add(file);
                    }
                }
            }

            foreach (var outDir in GetSourceChangeAffectedOutputDirectories())
            {
                foreach (var inDir in process.DirectoryDependencies)
                {
                    if (inDir.Path.IsWithin(m_pathTable, outDir))
                    {
                        FileUtilities.EnumerateDirectoryEntries(
                            inDir.Path.ToString(m_pathTable),
                            true,
                            (dir, fileName, attributes) =>
                            {
                                if (attributes == FileAttributes.Archive)
                                {
                                    var fullPath = Path.Combine(dir, fileName);
                                    changeAffectedInputs.Add(new FileArtifact(AbsolutePath.Create(m_pathTable, fullPath)));
                                }
                            });
                    }
                }
            }

            return ReadOnlyArray<FileArtifact>.FromWithoutCopy(changeAffectedInputs.ToArray());
        }

        /// <summary>
        /// Check if there is any output of this pip are affected by the source change
        /// </summary>
        private bool IsOutputAffectedBySourceChange(
            PathTable pathTable,
            Pip pip,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations)
        {
            switch (pip.PipType)
            {
                case PipType.Process:
                    return IsOutputOfProcessAffectedBySourceChange((Process)pip, pathTable, dynamicallyObservedFiles, dynamicallyObservedEnumerations);
                case PipType.CopyFile:
                    var sourceChangeAffectedOutputFiles = GetSourceChangeAffectedOutputs();
                    return m_sourceChangeAffectedOutputFiles == null ? false : m_sourceChangeAffectedOutputFiles.Contains(((CopyFile)pip).Source);
                default:
                    return false;
            }
        }

        private bool IsOutputOfProcessAffectedBySourceChange(
            Process process,
            PathTable pathTable,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations)
        {
            // sourceChangeAffectedOutputFiles and sourceChangeAffectedOutputDirectroies 
            // are initilized with the source change list.
            // If it is null, that means no file or directory change for this build.
            // Thus no output is affected by the change.

            var sourceChangeAffectedOutputFiles = GetSourceChangeAffectedOutputs();
            var sourceChangeAffectedOutputDirectroies = GetSourceChangeAffectedOutputDirectories();

            if ((sourceChangeAffectedOutputFiles == null || !sourceChangeAffectedOutputFiles.Any())
                && (sourceChangeAffectedOutputDirectroies == null || !sourceChangeAffectedOutputDirectroies.Any()))
            {
                return false;
            }

            // Check if any dynamic and static file dependency is sourceChangeAffectedOutputFiles
            var hasAffected = sourceChangeAffectedOutputFiles.Select(f => f.Path).Intersect(dynamicallyObservedFiles).Any()
                || sourceChangeAffectedOutputFiles.Intersect(process.Dependencies).Any();

            if (hasAffected)
            {
                return true;
            }

            if (dynamicallyObservedEnumerations.Any() || process.DirectoryDependencies.Any())
            {
                // Check if dynamic and static directory dependencies under any directory of sourceChangeAffectedOutputDirectroies
                foreach (var affectedDir in sourceChangeAffectedOutputDirectroies)
                {
                    foreach (var dynamicDir in dynamicallyObservedEnumerations)
                    {
                        if (dynamicDir.IsWithin(pathTable, affectedDir))
                        {
                            return true;
                        }
                    }

                    foreach (var staticDir in process.DirectoryDependencies)
                    {
                        if (staticDir.Path.IsWithin(pathTable, affectedDir))
                        {
                            return true;
                        }
                    }
                }

                // check if the dynamic or static directroy dependency contain any file from sourceChangeAffectedOutputFiles
                foreach (var affectedFile in sourceChangeAffectedOutputFiles)
                {
                    foreach (var dynamicDir in dynamicallyObservedEnumerations)
                    {
                        if (affectedFile.Path.IsWithin(pathTable, dynamicDir))
                        {
                            return true;
                        }
                    }

                    foreach (var staticDir in process.DirectoryDependencies)
                    {
                        if (affectedFile.Path.IsWithin(pathTable, staticDir.Path))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        public void ReportSourceChangeAffectedOutputs(
            PipResultStatus status,
            Pip pip,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations,
            IReadOnlyCollection<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)> outputContent = null,
            IReadOnlyCollection<DirectoryArtifact> directoryOutputs = null)
        {
            if (IsOutputAffectedBySourceChange(m_pathTable, pip, dynamicallyObservedFiles, dynamicallyObservedEnumerations))
            {
                if (outputContent != null)
                {
                    foreach (var output in outputContent)
                    {
                        if (output.Item3 == PipOutputOrigin.Produced)
                        {
                            m_sourceChangeAffectedOutputFiles.GetOrAdd(output.Item1);
                        }
                    }
                }

                if (directoryOutputs != null)
                {
                    foreach (var directoryArtifact in directoryOutputs)
                    {
                        m_sourceChangeAffectedOutputDirectories.GetOrAdd(directoryArtifact);
                    }
                }

            }
        }
    }
}
