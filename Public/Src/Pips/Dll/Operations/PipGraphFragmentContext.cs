using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// PipGraphFragmentContext
    /// </summary>
    public class PipGraphFragmentContext
    {
        private ConcurrentBigMap<DirectoryArtifact, DirectoryArtifact> m_directoryMap = new ConcurrentBigMap<DirectoryArtifact, DirectoryArtifact>();

        private ConcurrentBigMap<uint, uint> m_PipIdValueMap = new ConcurrentBigMap<uint, uint>();


        internal uint Remap(uint pipIdValue)
        {
            if (m_PipIdValueMap.TryGetValue(pipIdValue, out var mappedPipIdValue))
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
        /// PipGraphFragmentContext
        /// </summary>
        public void AddDirectoryMapping(DirectoryArtifact oldDirectory, DirectoryArtifact mappedDirectory)
        {
            m_directoryMap[oldDirectory] = mappedDirectory;
        }

        /// <summary>
        /// PipGraphFragmentContext
        /// </summary>
        public void AddPipIdMapping(uint oldPipIdValue, uint newPipIdValue)
        {
            m_PipIdValueMap[oldPipIdValue] = newPipIdValue;
        }
    }
}
