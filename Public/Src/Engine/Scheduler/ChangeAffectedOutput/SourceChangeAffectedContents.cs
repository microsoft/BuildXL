// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Storage.InputChange;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.ChangeAffectedOutput
{
    /// <summary>
    /// Class representing source change affected output list.
    /// </summary>
    public sealed class SourceChangeAffectedContents
    {
        private readonly PathTable m_pathTable;
        private readonly FileContentManager m_fileContentManager;

        /// <summary>
        /// Output file contents which affected by the source change. 
        /// </summary>
        /// <remarks>
        /// This list should be initialized by the input change list provided from the InputChanges configuration option.
        /// On the master, this list contains all the affected output contents of the build and is maintained by cally the function ReportSourceChangeAffectedOutputs() after execution of each pip.
        /// On the workers, this list contains all the affected files distributed to the worker. It is maintained calling the function ReportSourceChangedAffectedFile() when receiving a file from master by worker.  
        /// </remarks>
        private ConcurrentBigSet<AbsolutePath> m_sourceChangeAffectedFiles = new ConcurrentBigSet<AbsolutePath>();

        /// <nodoc />
        public SourceChangeAffectedContents(PathTable pathTable, FileContentManager fileContentManager)
        {
            m_pathTable = pathTable;
            m_fileContentManager = fileContentManager;
    }

        /// <summary>
        /// Initial affected output list with the external source change list
        /// </summary>
        public void InitialAffectedOutputList(InputChangeList inputChangeList, PathTable pathTable)
        {
            foreach (var changePath in inputChangeList.ChangedPaths)
            {
                switch (changePath.PathChanges)
                {
                    case PathChanges.DataOrMetadataChanged:
                    case PathChanges.NewlyPresentAsFile:
                        m_sourceChangeAffectedFiles.GetOrAdd(new FileArtifact(AbsolutePath.Create(pathTable, changePath.Path)));
                        break;
                }

            }
        }

        /// <summary>
        /// Compute the intersection of the pip's dependencies and global affected outputs of the build
        /// </summary>
        /// 
        public IReadOnlyCollection<AbsolutePath> GetChangeAffectedInputs(Process process)
        {
            var changeAffectedInputs = new HashSet<AbsolutePath>();

            changeAffectedInputs.AddRange(process.Dependencies.Select(f => f.Path).Where(f=>m_sourceChangeAffectedFiles.Contains(f)));

            foreach (var directory in process.DirectoryDependencies)
            {
                foreach (var file in m_fileContentManager.ListSealedDirectoryContents(directory))
                {
                    if (m_sourceChangeAffectedFiles.Contains(file))
                    {
                        changeAffectedInputs.Add(file.Path);
                    }
                }
            }

            return ReadOnlyArray<AbsolutePath>.FromWithoutCopy(changeAffectedInputs.ToArray());
        }

        /// <summary>
        /// Check if the outputs of this pip are affected by the source change
        /// </summary>
        private bool IsOutputAffectedBySourceChange(
            Pip pip,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles)
        {
            switch (pip.PipType)
            {
                case PipType.Process:
                    return IsOutputOfProcessAffectedBySourceChange((Process)pip, dynamicallyObservedFiles);
                case PipType.CopyFile:
                    return m_sourceChangeAffectedFiles.Contains(((CopyFile)pip).Source);
                default:
                    return false;
            }
        }

        private bool IsOutputOfProcessAffectedBySourceChange(
            Process process,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles)
        {
            // sourceChangeAffectedOutputFiles is initilized with the source change list.
            // If it is null, that means no file change for this build.
            // Thus no output is affected by the change.
            if (m_sourceChangeAffectedFiles.Count == 0)
            {
                return false;
            }

            return dynamicallyObservedFiles.Concat(process.Dependencies.Select(f=>f.Path)).Any(f => m_sourceChangeAffectedFiles.Contains(f));
        }

        /// <summary>
        /// This function executes on the master to maintain the globle source change affected output list m_sourceChangeAffectedOutputFiles
        /// </summary>
        public void ReportSourceChangeAffectedFiles(
            Pip pip,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<FileArtifact> outputContents)
        {
            if (IsOutputAffectedBySourceChange(pip, dynamicallyObservedFiles))
            {
                foreach (var o in outputContents)
                {
                    m_sourceChangeAffectedFiles.Add(o);
                }
            }
        }

        /// <summary>
        /// Check if this file is a change affected file
        /// </summary>
        public bool IsSourceChangedAffectedFile(AbsolutePath path)
        {
            return m_sourceChangeAffectedFiles.Contains(path);
        }
        /// <summary>
        /// This function execute on worker
        /// </summary>
        public void ReportSourceChangedAffectedFile(AbsolutePath path)
        {
            m_sourceChangeAffectedFiles.Add(path);
        }
    }
}
