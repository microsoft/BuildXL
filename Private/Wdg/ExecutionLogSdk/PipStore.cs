// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Engine;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Implements an internal class that stores a set of pip descriptors and provides dictionaries that allow retrieving pip descriptors based on a pip id or a pip name
    /// </summary>
    internal sealed class PipStore
    {
        #region Private properties

        /// <summary>
        /// Private dictionary that maps pip Ids to pip descriptor objects
        /// </summary>
        private readonly ConcurrentDictionary<uint, PipDescriptor> m_pipIdDictionary;

        /// <summary>
        /// Private dictionary that maps pip names to pip descriptor objects (pip name are no unique, therefore each pip name is mapped to a list of pip descriptors)
        /// </summary>
        private readonly FullSymbolConcurrentDictionary<IReadOnlyCollection<PipDescriptor>> m_pipNameDictionary;
        #endregion

        #region Internal properties

        /// <summary>
        /// Dictionary that maps pip Ids to pip descriptor objects
        /// </summary>
        internal IReadOnlyDictionary<uint, PipDescriptor> PipIdDictionary { get { return (IReadOnlyDictionary<uint, PipDescriptor>)m_pipIdDictionary; } }

        /// <summary>
        /// Dictionary that maps pip names to pip descriptor objects (pip name are no unique, therefore each pip name is mapped to a list of pip descriptors)
        /// </summary>
        internal IReadOnlyDictionary<string, IReadOnlyCollection<PipDescriptor>> PipNameDictionary { get { return (IReadOnlyDictionary<string, IReadOnlyCollection<PipDescriptor>>)m_pipNameDictionary; } }

        /// <summary>
        /// Thread safe indexing method that returns elements based on the a pip Id
        /// </summary>
        /// <param name="pipId">The pip Id to locate</param>
        /// <returns>The PipDescriptor that matches the pip id</returns>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal PipDescriptor this[uint pipId]
        {
            get
            {
                return m_pipIdDictionary[pipId];
            }
        }

        /// <summary>
        /// Thread safe indexing method that returns elements based on the a pip name
        /// </summary>
        /// <param name="pipName">The pip name to locate</param>
        /// <returns>List of PipDescriptors that match the pip name</returns>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal IReadOnlyCollection<PipDescriptor> this[string pipName]
        {
            get
            {
                return (IReadOnlyCollection<PipDescriptor>)m_pipNameDictionary[pipName];
            }
        }

        /// <summary>
        /// Returns the number of pip descriptors in the pip store
        /// </summary>
        internal int Count
        {
            get
            {
                return m_pipIdDictionary.Count;
            }
        }

        /// <summary>
        /// Enumerates all pip Ids
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal IEnumerable<uint> PipIds
        {
            get
            {
                return m_pipIdDictionary.Keys;
            }
        }

        /// <summary>
        /// Enumerates all pip names
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal IEnumerable<string> PipNames
        {
            get
            {
                return m_pipNameDictionary.Keys;
            }
        }
        #endregion

        #region Internal methods

        /// <summary>
        /// Internal constructor
        /// </summary>
        internal PipStore(SymbolTable symbolTable)
        {
            m_pipIdDictionary = new ConcurrentDictionary<uint, PipDescriptor>();
            m_pipNameDictionary = new FullSymbolConcurrentDictionary<IReadOnlyCollection<PipDescriptor>>(symbolTable);
        }

        /// <summary>
        /// Thread safe GetOrAdd method. We use concurrent dictionaries for backing, therefore no lock is needed.
        /// </summary>
        /// <param name="pipId">The pip Id to be used when creating a new pip descriptor</param>
        /// <param name="pipName">The pip name to be used when creating a new pip descriptor</param>
        /// <returns>New PipDescriptor instance that stores the data from the specified pip</returns>
        internal PipDescriptor SynchronizedGetOrAdd(Process fullPip, CachedGraph buildGraph,
            ExecutionLogLoadOptions loadOptions, ConcurrentHashSet<FileDescriptor> emptyConcurrentHashSetOfFileDescriptor,
            ConcurrentHashSet<PipDescriptor> emptyConcurrentHashSetOfPipDescriptor, ConcurrentHashSet<ProcessInstanceDescriptor> emptyConcurrentHashSetOfReportedProcesses,
            StringIdEnvVarDictionary emptyStringIDEnvVarDictionary, AbsolutePathConcurrentHashSet emptyAbsolutePathConcurrentHashSet)
        {
            PipDescriptor newItem = m_pipIdDictionary.GetOrAdd(fullPip.PipId.Value, (p) => { return new PipDescriptor(fullPip, buildGraph, loadOptions, emptyConcurrentHashSetOfFileDescriptor, emptyConcurrentHashSetOfPipDescriptor, emptyConcurrentHashSetOfReportedProcesses, emptyStringIDEnvVarDictionary, emptyAbsolutePathConcurrentHashSet); });

            IReadOnlyCollection<PipDescriptor> pipList = m_pipNameDictionary.GetOrAdd(fullPip.Provenance.OutputValueSymbol, new ConcurrentHashSet<PipDescriptor>());

            // This is pretty ugly: Doing down casting here so we can add elements to our read only collection
            // The collection is read only because we do no want to allow the Users of the SDK to change it. Unfortunately the only way .NET allows me to define such dictionary
            // is to specify its elements as a IReadOnlyCollection and down cast every time I need to modify it.
            // Down casting here is pretty safe though. The collection is only created in this method and we know that it is always a ConcurrentDictionary.
            (pipList as ConcurrentHashSet<PipDescriptor>).Add(newItem);

            return newItem;
        }

        /// <summary>
        /// Thread safe TryGetValue method based on a pip Id
        /// </summary>
        /// <param name="keyPipId">The pip Id to locate</param>
        /// <param name="value">The PipDescriptor that matches the pip id</param>
        /// <returns>true, when a mathcing PipDescriptor is found, false otherwise</returns>
        internal bool SynchronizedTryGetValue(uint keyPipId, out PipDescriptor value)
        {
            return m_pipIdDictionary.TryGetValue(keyPipId, out value);
        }

        /// <summary>
        /// Checks if there is a pip descriptor in the collection with a given pip Id
        /// </summary>
        /// <param name="pipId">The pip Id to locate</param>
        /// <returns>true, when there is a pip descriptor with a matching pip Id, false otherwise</returns>
        internal bool ContainsPipId(uint pipId)
        {
            return m_pipIdDictionary.ContainsKey(pipId);
        }

        /// <summary>
        /// Checks if there is a pip descriptor in the collection with a given pip name
        /// </summary>
        /// <param name="pipName">The pip name to locate</param>
        /// <returns>true, when there is a pip descriptor with a matching pip Id, false otherwise</returns>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal bool ContainsPipName(string pipName)
        {
            return m_pipNameDictionary.ContainsKey(pipName);
        }
        #endregion
    }
}
