// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Result of checking
    /// </summary>
    public sealed class RunnableFromCacheResult
    {
        private readonly CacheHitData m_cacheHit;

        /// <summary>
        /// The weak fingerprint as a generic fingerprint
        /// </summary>
        public ContentFingerprint Fingerprint => WeakFingerprint.ToGenericFingerprint();

        /// <summary>
        /// Weak fingerprint
        /// </summary>
        public readonly WeakContentFingerprint WeakFingerprint;

        /// <summary>
        /// The list of dynamically observed files. i.e., the enumerations that were not in the graph, but should be considered for invalidating incremental scheduling state.
        /// </summary>
        public readonly ReadOnlyArray<AbsolutePath> DynamicallyObservedFiles;

        /// <summary>
        /// The list of dynamically observed enumerations. i.e., the enumerations that were not in the graph, but should be considered for invalidating incremental scheduling state.
        /// </summary>
        public readonly ReadOnlyArray<AbsolutePath> DynamicallyObservedEnumerations;

        /// <summary>
        /// Observed allowed undeclared source reads
        /// </summary>
        public readonly IReadOnlySet<AbsolutePath> AllowedUndeclaredReads;

        /// <summary>
        /// Absent path probes under opaque directory roots
        /// </summary>
        public readonly IReadOnlySet<AbsolutePath> AbsentPathProbesUnderNonDependenceOutputDirectories;

        /// <summary>
        /// Fields which are specific to a usable cache hit.
        /// </summary>
        public sealed class CacheHitData
        {
            /// <summary>
            /// Path set hash
            /// </summary>
            public readonly ContentHash PathSetHash;

            /// <summary>
            /// PathSet
            /// </summary>
            public readonly ObservedPathSet? PathSet;

            /// <summary>
            /// Strong fingerprint
            /// </summary>
            public readonly StrongContentFingerprint StrongFingerprint;

            /// <summary>
            /// Metadata part of the existing cache descriptor. In particular, some things need
            /// <see cref="PipCacheDescriptorV2Metadata.NumberOfWarnings" />. Note that this field
            /// is always present even if a V1 descriptor was actually found (the right parts of the
            /// old-style descriptor are copied over).
            /// </summary>
            public readonly PipCacheDescriptorV2Metadata Metadata;

            /// <summary>
            /// Metadata content hash
            /// </summary>
            public readonly ContentHash MetadataHash;

            /// <summary>
            /// Content hashes of cached artifacts.
            /// </summary>
            [SuppressMessage("Microsoft.Security", "CA2105:ArrayFieldsShouldNotBeReadOnly")]
            public readonly (FileArtifact fileArtifact, FileMaterializationInfo fileMaterializationInfo)[] CachedArtifactContentHashes;

            /// <summary>
            /// The contents of dynamic directories (segments of <see cref="CachedArtifactContentHashes"/>)
            /// </summary>
            [SuppressMessage("Microsoft.Security", "CA2105:ArrayFieldsShouldNotBeReadOnly")]
            public readonly ArrayView<(FileArtifact fileArtifact, FileMaterializationInfo fileMaterializationInfo)>[] DynamicDirectoryContents;

            /// <summary>
            /// Absent artifacts.
            /// </summary>
            public readonly IReadOnlyList<FileArtifact> AbsentArtifacts;

            /// <summary>
            /// Standard output.
            /// </summary>
            public readonly Tuple<AbsolutePath, ContentHash, string> StandardOutput;

            /// <summary>
            /// Standard input.
            /// </summary>
            public readonly Tuple<AbsolutePath, ContentHash, string> StandardError;

            /// <summary>
            /// Indicates if this was a remote or local hit.
            /// </summary>
            public readonly PublishedEntryRefLocality Locality;

            /// <nodoc />
            public CacheHitData(
                ContentHash pathSetHash,
                StrongContentFingerprint strongFingerprint,
                PipCacheDescriptorV2Metadata metadata,
                (FileArtifact, FileMaterializationInfo)[] cachedArtifactContentHashes,
                ArrayView<(FileArtifact, FileMaterializationInfo)>[] dynamicDirectoryContents,
                IReadOnlyList<FileArtifact> absentArtifacts,
                Tuple<AbsolutePath, ContentHash, string> standardOutput,
                Tuple<AbsolutePath, ContentHash, string> standardError,
                PublishedEntryRefLocality locality,
                ContentHash metadataHash,
                ObservedPathSet? pathSet)
            {
                PathSetHash = pathSetHash;
                StrongFingerprint = strongFingerprint;
                Metadata = metadata;
                CachedArtifactContentHashes = cachedArtifactContentHashes;
                DynamicDirectoryContents = dynamicDirectoryContents;
                AbsentArtifacts = absentArtifacts;
                StandardOutput = standardOutput;
                StandardError = standardError;
                Locality = locality;
                MetadataHash = metadataHash;
                PathSet = pathSet;
            }
        }

        /// <nodoc />
        private RunnableFromCacheResult(
            WeakContentFingerprint weakFingerprint,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations,
            IReadOnlySet<AbsolutePath> allowedUndeclaredSourceReads,
            IReadOnlySet<AbsolutePath> absentPathProbesUnderNonDependenceOutputDirectories,
            CacheHitData cacheHitData)
        {
            WeakFingerprint = weakFingerprint;
            DynamicallyObservedFiles = dynamicallyObservedFiles;
            DynamicallyObservedEnumerations = dynamicallyObservedEnumerations;
            AllowedUndeclaredReads = allowedUndeclaredSourceReads;
            AbsentPathProbesUnderNonDependenceOutputDirectories = absentPathProbesUnderNonDependenceOutputDirectories;
            m_cacheHit = cacheHitData; // Maybe null for a miss.
        }

        /// <summary>
        /// Creates a result for a miss (or unusable descriptor)
        /// </summary>
        public static RunnableFromCacheResult CreateForMiss(WeakContentFingerprint weakFingerprint)
        {
            return new RunnableFromCacheResult(
                weakFingerprint,
                dynamicallyObservedFiles: ReadOnlyArray<AbsolutePath>.Empty,
                dynamicallyObservedEnumerations: ReadOnlyArray<AbsolutePath>.Empty,
                allowedUndeclaredSourceReads: CollectionUtilities.EmptySet<AbsolutePath>(),
                absentPathProbesUnderNonDependenceOutputDirectories: CollectionUtilities.EmptySet<AbsolutePath>(),
                null);
        }

        /// <summary>
        /// Creates a result for a usable descriptor.
        /// </summary>
        public static RunnableFromCacheResult CreateForHit(
            WeakContentFingerprint weakFingerprint,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations,
            IReadOnlySet<AbsolutePath> allowedUndeclaredSourceReads,
            IReadOnlySet<AbsolutePath> absentPathProbesUnderNonDependenceOutputDirectories,
            CacheHitData cacheHitData)
        {
            Contract.Requires(cacheHitData != null);
            return new RunnableFromCacheResult(
                weakFingerprint,
                dynamicallyObservedFiles,
                dynamicallyObservedEnumerations,
                allowedUndeclaredSourceReads,
                absentPathProbesUnderNonDependenceOutputDirectories,
                cacheHitData);
        }

        /// <summary>
        /// True if pip can run from cache. If so, data specific to the cache hit can be retrieved
        /// with <see cref="GetCacheHitData" />.
        /// </summary>
        public bool CanRunFromCache => m_cacheHit != null;

        /// <summary>
        /// Gets the <see cref="CacheHitData" /> associated with the cache lookup.
        /// This is only available if the cache lookup succeeded (<see cref="CanRunFromCache" />).
        /// </summary>
        public CacheHitData GetCacheHitData()
        {
            Contract.Requires(CanRunFromCache);
            return m_cacheHit;
        }
    }
}
