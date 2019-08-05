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
        private ConcurrentBigMap<DirectoryArtifact, DirectoryArtifact> m_directoryMap = new ConcurrentBigMap<DirectoryArtifact, DirectoryArtifact>();

        /// <summary>
        /// Add directory mapping, so all pips using the old directory artifact get mapped to the new one.
        /// </summary>
        public void AddDirectoryMapping(DirectoryArtifact oldDirectory, DirectoryArtifact mappedDirectory)
        {
            bool added = m_directoryMap.TryAdd(oldDirectory, mappedDirectory);
            Contract.Assert(added);
        }

        /// <summary>
        /// Get the DirectoryArtifact corresponding to a pip fragment DirectoryArtifact.
        /// </summary>
        internal DirectoryArtifact RemapDirectory(DirectoryArtifact directoryArtifact)
        {
            if (m_directoryMap.TryGetValue(directoryArtifact, out var mappedDirectory))
            {
                return mappedDirectory;
            }

            return directoryArtifact;
        }
    }
}
