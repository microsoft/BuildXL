// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// PipGraphFragmentContext, used to remap directory arifacts and pip ids to values which are assigned after the pips are added to the graph.
    /// </summary>
    public class PipGraphFragmentContext
    {
        private ConcurrentBigMap<DirectoryArtifact, DirectoryArtifact> m_directoryMap = new ConcurrentBigMap<DirectoryArtifact, DirectoryArtifact>();

        private ConcurrentBigMap<uint, uint> m_pipIdValueMap = new ConcurrentBigMap<uint, uint>();

        internal uint Remap(uint pipIdValue)
        {
            if (m_pipIdValueMap.TryGetValue(pipIdValue, out var mappedPipIdValue))
            {
                return mappedPipIdValue;
            }

            return pipIdValue;
        }

        internal DirectoryArtifact Remap(DirectoryArtifact directoryArtifact)
        {
            if (m_directoryMap.TryGetValue(directoryArtifact, out var mappedDirectory))
            {
                return mappedDirectory;
            }

            return directoryArtifact;
        }

        /// <summary>
        /// Add directory mapping, so all pips using the old directory artifact get mapped to the new one.
        /// </summary>
        public void AddDirectoryMapping(DirectoryArtifact oldDirectory, DirectoryArtifact mappedDirectory)
        {
            m_directoryMap[oldDirectory] = mappedDirectory;
        }

        /// <summary>
        /// Add a pip id mapping, so all pips specifying a service pip dependency on the old pip will get mapped to the new pip value.
        /// </summary>
        public void AddPipIdMapping(uint oldPipIdValue, uint newPipIdValue)
        {
            m_pipIdValueMap[oldPipIdValue] = newPipIdValue;
        }
    }
}
