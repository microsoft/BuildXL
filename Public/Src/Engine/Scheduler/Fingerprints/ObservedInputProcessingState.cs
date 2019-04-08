// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// State bag used when processing observed inputs after execution and during caching
    /// </summary>
    internal class ObservedInputProcessingState : IDisposable
    {
        private static readonly ObjectPool<ObservedInputProcessingState> s_pool = new ObjectPool<ObservedInputProcessingState>(
            () => new ObservedInputProcessingState(),
            state => state.Clear());

        // NOTE: Be sure to add any collections created here to Clear
        public readonly List<AbsolutePath> DynamicallyObservedFiles = new List<AbsolutePath>();
        public readonly HashSet<AbsolutePath> AllowedUndeclaredReads = new HashSet<AbsolutePath>();
        public readonly HashSet<AbsolutePath> AbsentPathProbesUnderNonDependenceOutputDirectories = new HashSet<AbsolutePath>();
        public readonly List<AbsolutePath> DynamicallyObservedEnumerations = new List<AbsolutePath>();
        public readonly List<SourceSealWithPatterns> SourceDirectoriesAllDirectories = new List<SourceSealWithPatterns>();
        public readonly List<SourceSealWithPatterns> SourceDirectoriesTopDirectoryOnly = new List<SourceSealWithPatterns>();
        public readonly HashSet<AbsolutePath> ObservationArtifacts = new HashSet<AbsolutePath>();
        public readonly HashSet<AbsolutePath> DirectoryDependencyContentsFilePaths = new HashSet<AbsolutePath>();
        public readonly Dictionary<AbsolutePath, (DirectoryMembershipFilter, DirectoryEnumerationMode)> EnumeratedDirectories = new Dictionary<AbsolutePath, (DirectoryMembershipFilter, DirectoryEnumerationMode)>();
        public readonly HashSet<HierarchicalNameId> AllDependencyPathIds = new HashSet<HierarchicalNameId>();
        public readonly HashSet<AbsolutePath> SearchPaths = new HashSet<AbsolutePath>();

        /// <summary>
        /// Gets a pooled instance instance of <see cref="ObservedInputProcessingState"/>.
        /// </summary>
        public static ObservedInputProcessingState GetInstance()
        {
            return s_pool.GetInstance().Instance;
        }

        private void Clear()
        {
            DynamicallyObservedFiles.Clear();
            AllowedUndeclaredReads.Clear();
            AbsentPathProbesUnderNonDependenceOutputDirectories.Clear();
            DynamicallyObservedEnumerations.Clear();
            SourceDirectoriesAllDirectories.Clear();
            SourceDirectoriesTopDirectoryOnly.Clear();
            ObservationArtifacts.Clear();
            DirectoryDependencyContentsFilePaths.Clear();
            EnumeratedDirectories.Clear();
            AllDependencyPathIds.Clear();
            SearchPaths.Clear();
        }

        /// <summary>
        /// Returns the instance to the pool
        /// </summary>
        public void Dispose()
        {
            s_pool.PutInstance(this);
        }
    }
}
