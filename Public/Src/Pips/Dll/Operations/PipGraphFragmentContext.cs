// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// PipGraphFragmentContext used to remap directory arifacts.
    /// </summary>
    public class PipGraphFragmentContext
    {
        /// <summary>
        /// Maps a directory artifact from the serialized version to the directory artifact in the pip graph.
        /// All uses of that directory artifact will get remapped to the new value.
        /// </summary>
        private readonly ConcurrentBigMap<DirectoryArtifact, DirectoryArtifact> m_directoryMap = new ConcurrentBigMap<DirectoryArtifact, DirectoryArtifact>();

        /// <summary>
        /// Maps an old pip id to a new pip id.
        /// All uses of that pip id will get remapped to the new value.
        /// </summary>
        /// <remarks>
        /// This new pip id is imporatant for handling service and IPC pips where the relation between them are represented as pip id.
        /// </remarks>
        private readonly ConcurrentBigMap<PipId, PipId> m_pipIdMap = new ConcurrentBigMap<PipId, PipId>();

        /// <summary>
        /// Adds a directory artifact mapping.
        /// </summary>
        public void AddDirectoryMapping(DirectoryArtifact oldDirectory, DirectoryArtifact mappedDirectory)
        {
            bool added = m_directoryMap.TryAdd(oldDirectory, mappedDirectory);
            Contract.Assert(added);
        }

        /// <summary>
        /// Gets remapped directory artifact.
        /// </summary>
        internal DirectoryArtifact RemapDirectory(DirectoryArtifact directoryArtifact) => 
            m_directoryMap.TryGetValue(directoryArtifact, out var mappedDirectory) ? mappedDirectory : directoryArtifact;

        /// <summary>
        /// Adds a pip id mapping.
        /// </summary>
        public void AddPipIdMapping(PipId oldPipId, PipId newPipId)
        {
            bool added = m_pipIdMap.TryAdd(oldPipId, newPipId);
            Contract.Assert(added);
        }

        /// <summary>
        /// Gets remapped pip id.
        /// </summary>
        internal PipId RemapPipId(PipId pipId) =>
            m_pipIdMap.TryGetValue(pipId, out var mappedPipId) ? mappedPipId : pipId;
    }
}
