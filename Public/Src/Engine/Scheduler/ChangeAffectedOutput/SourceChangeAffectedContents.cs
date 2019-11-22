// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Collections.Concurrent;
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
    /// Class representing source change affected output or inputs.
    /// </summary>
    public sealed class SourceChangeAffectedInputs
    {
        private PathTable PathTable => m_fileContentManager.Host.Context.PathTable;

        private readonly FileContentManager m_fileContentManager;

        /// <summary>
        /// Files which affected by the source change. 
        /// </summary>
        /// <remarks>
        /// This list should be initialized by the input change list provided from the InputChanges configuration option.
        /// On the master, this list contains all the affected files of the build. It is maintained by calling the function ReportSourceChangeAffectedOutputs() after execution of each pip.
        /// On the workers, this list contains all the affected files distributed to the worker. It is maintained by calling the function ReportSourceChangedAffectedFile() when reporting a file input from master by worker.  
        /// </remarks>
        private readonly ConcurrentBigSet<AbsolutePath> m_sourceChangeAffectedFiles = new ConcurrentBigSet<AbsolutePath>();

        /// <summary>
        /// Initial source changes.
        /// </summary>
        /// <remarks>
        /// We keep initial source changes for computing changes that reside in source seal directories.
        /// </remarks>
        private readonly HashSet<AbsolutePath> m_initialSourceChanges = new HashSet<AbsolutePath>();

        /// <summary>
        /// Mappings from source seal directory to their affected members.
        /// </summary>
        private readonly ConcurrentDictionary<DirectoryArtifact, List<AbsolutePath>> m_sourceSealDirectoryAffectedMembers = new ConcurrentDictionary<DirectoryArtifact, List<AbsolutePath>>();

        /// <nodoc />
        public SourceChangeAffectedInputs(FileContentManager fileContentManager)
        {
            m_fileContentManager = fileContentManager;
        }

        /// <summary>
        /// Initial affected output list with the external source change list
        /// </summary>
        public void InitialAffectedOutputList(InputChangeList inputChangeList, PathTable pathTable)
        {
            if (inputChangeList == null)
            {
                return;
            }

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

            m_initialSourceChanges.AddRange(m_sourceChangeAffectedFiles.UnsafeGetList());
        }

        /// <summary>
        /// Compute the intersection of the pip's dependencies and the affected contents of the build
        /// </summary>
        public IReadOnlyList<AbsolutePath> GetChangeAffectedInputs(Process process)
        {
            var changeAffectedInputs = new HashSet<AbsolutePath>(process.Dependencies.Select(f => f.Path).Where(p => m_sourceChangeAffectedFiles.Contains(p)));

            foreach (var directory in process.DirectoryDependencies)
            {
                foreach (var file in m_fileContentManager.ListSealedDirectoryContents(directory))
                {
                    if (m_sourceChangeAffectedFiles.Contains(file))
                    {
                        changeAffectedInputs.Add(file.Path);
                    }
                }

                if (m_fileContentManager.Host.TryGetSourceSealDirectory(directory, out var sourceSealWithPatterns))
                {
                    var affectedMembers = m_sourceSealDirectoryAffectedMembers.GetOrAdd(
                        directory,
                        d => new List<AbsolutePath>(m_initialSourceChanges.Where(p => sourceSealWithPatterns.Contains(PathTable, p))));
                    changeAffectedInputs.AddRange(affectedMembers);
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
        /// This function executes on the master to maintain the globle source change affected contents
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
