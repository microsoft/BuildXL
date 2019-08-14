// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// PipGraphFragmentContext, used to remap directory arifacts and pip ids to values which are assigned after the pips are added to the graph.
    /// </summary>
    public class PipGraphFragmentContext
    {
        /// <summary>
        /// Maps a directory artifact from the serialized version to the directory artifact in the pip graph.
        /// All uses of that directory artifact will get remapped to the new value.
        /// </summary>
        private ConcurrentBigMap<DirectoryArtifact, DirectoryArtifact> m_directoryMap = new ConcurrentBigMap<DirectoryArtifact, DirectoryArtifact>();

        /// <summary>
        /// Maps a pip ID value from the serialized version to the pip ID value in the pip graph.
        /// All uses of that pip ID will get ramapped to the new value
        /// </summary>
        private ConcurrentBigMap<uint, uint> m_pipIdValueMap = new ConcurrentBigMap<uint, uint>();

        /// <summary>
        /// Maps a directory artifact from the variable name representing the serialized version to the directory artifact in the pip graph.
        /// All uses of that directory artifact variable name will get remapped to the new value.
        /// Variables names should be used for partially sealled directories which need to be used by other fragments.
        /// The directory map should be used by opaques which can be accessed by path, or sealled directories which don't need to be used outside the fragment.
        /// </summary>
        private ConcurrentBigMap<FullSymbol, DirectoryArtifact> m_variableNameToDirectoryMap = new ConcurrentBigMap<FullSymbol, DirectoryArtifact>();

        /// <summary>
        /// Maps directory artifact to variable name used to represent it.
        /// This is populated by whomever creates the pip graph fragment so that multiple fragments can all reference the same sealed directory without having to know exactly which pip id to use.
        /// </summary>
        private ConcurrentBigMap<DirectoryArtifact, FullSymbol> m_directoryToVariableNameMap = new ConcurrentBigMap<DirectoryArtifact, FullSymbol>();

        /// <summary>
        /// Maps pip id variable names from the string based variable name it had in serialized pip fragment, to the uint value is has when added to the graph.
        /// All uses of that pip id variable name will get remapped to the new value.
        /// </summary>
        private ConcurrentBigMap<FullSymbol, uint> m_variableNameToPipIdValueMap = new ConcurrentBigMap<FullSymbol, uint>();

        /// <summary>
        /// Maps pip ids to variable names to represent those Ids.
        /// This is populated by whomever creates the pip graph fragment so that multiple fragments can all reference the same pip id, without having to know exactly which pip id to use.
        /// </summary>
        private ConcurrentBigMap<uint, FullSymbol> m_pipIdValueToVariableNameMap = new ConcurrentBigMap<uint, FullSymbol>();

        /// <summary>
        /// Add directory mapping, so all pips using the old directory artifact get mapped to the new one.
        /// </summary>
        public void AddPipIdValueMapping(uint oldPipIdValue, uint mappedPipIdValue)
        {
            bool added = m_pipIdValueMap.TryAdd(oldPipIdValue, mappedPipIdValue);
            Contract.Assert(added);
        }

        /// <summary>
        /// Add directory mapping, so all pips using the old directory artifact get mapped to the new one.
        /// </summary>
        public void AddDirectoryMapping(DirectoryArtifact oldDirectory, DirectoryArtifact mappedDirectory)
        {
            bool added = m_directoryMap.TryAdd(oldDirectory, mappedDirectory);
            Contract.Assert(added);
        }

        /// <summary>
        /// If the pipIdValue corresponds to a variable, return that variable
        /// </summary>
        internal bool TryGetVariableNameForPipIdValue(uint pipIdValue, out FullSymbol variableName)
        {
            return m_pipIdValueToVariableNameMap.TryGetValue(pipIdValue, out variableName);
        }

        /// <summary>
        /// If the directory corresponds to a variable, return that variable
        /// </summary>
        internal bool TryGetVariableNameForDirectory(DirectoryArtifact directory, out FullSymbol variableName)
        {
            return m_directoryToVariableNameMap.TryGetValue(directory, out variableName);
        }

        /// <summary>
        /// If the variable name corresponds to a pip id value, return that pip id value
        /// </summary>
        internal bool TryGetPipIdValueForVariableName(FullSymbol variableName, out uint pipIdValue)
        {
            return m_variableNameToPipIdValueMap.TryGetValue(variableName, out pipIdValue);
        }

        /// <summary>
        /// If the variable name corresponds to a directory artifact, return that directory
        /// </summary>
        internal bool TryGetDirectoryArtifactForVariableName(FullSymbol variableName, out DirectoryArtifact directory)
        {
            return m_variableNameToDirectoryMap.TryGetValue(variableName, out directory);
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

        /// <summary>
        /// Get the pip ID value corresponding to a pip fragment pip ID value.
        /// </summary>
        internal uint RemapPipIdValue(uint pipIdValue)
        {
            if (m_pipIdValueMap.TryGetValue(pipIdValue, out var mappedPipIdValue))
            {
                return mappedPipIdValue;
            }

            return pipIdValue;
        }

        /// <summary>
        /// Add directory mapping, so all pips using the old directory artifact get mapped to the new one.
        /// </summary>
        internal void AddDirectoryMapping(FullSymbol directoryVariableName, DirectoryArtifact mappedDirectory)
        {
            bool added = m_variableNameToDirectoryMap.TryAdd(directoryVariableName, mappedDirectory);
            Contract.Assert(added);

            added = m_directoryToVariableNameMap.TryAdd(mappedDirectory, directoryVariableName);
            Contract.Assert(added);
        }

        /// <summary>
        /// Add a mapping between variable name and pip id value.
        /// </summary>
        internal void AddPipIdValueMapping(FullSymbol pipIdVariableName, uint pipIdValue)
        {
            bool added = m_variableNameToPipIdValueMap.TryAdd(pipIdVariableName, pipIdValue);
            Contract.Assert(added);
            added = m_pipIdValueToVariableNameMap.TryAdd(pipIdValue, pipIdVariableName);
            Contract.Assert(added);
        }
    }
}
