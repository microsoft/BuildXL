// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
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
        /// The list of dynamically observed read files.
        /// </summary>
        public readonly ReadOnlyArray<(AbsolutePath Path, DynamicObservationKind Kind)> DynamicObservations;
        
        /// <summary>
        /// Observed allowed undeclared source reads
        /// </summary>
        public readonly IReadOnlySet<AbsolutePath> AllowedUndeclaredReads;

        /// <summary>
        /// Cache miss reason
        /// </summary>
        public readonly PipCacheMissType CacheMissType;

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
            /// Collection of directories that were succesfully created during pip execution. 
            /// </summary>
            /// <remarks>
            /// Observe there is no guarantee those directories still exist. However, there was a point during the execution of the associated pip when these directories 
            /// were not there, the running pip created them and the creation was successful. 
            /// Only populated if allowed undeclared reads is on, since these are used for computing directory fingerprint enumeration when undeclared files are allowed.
            /// </remarks>
            public readonly IReadOnlySet<AbsolutePath> CreatedDirectories;

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
                IReadOnlySet<AbsolutePath> createdDirectories,
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
                CreatedDirectories = createdDirectories;
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
            ReadOnlyArray<(AbsolutePath, DynamicObservationKind)> dynamicObservations,
            IReadOnlySet<AbsolutePath> allowedUndeclaredSourceReads,
            CacheHitData cacheHitData,
            PipCacheMissType cacheMissType)
        {
            WeakFingerprint = weakFingerprint;
            DynamicObservations = dynamicObservations;
            AllowedUndeclaredReads = allowedUndeclaredSourceReads;
            m_cacheHit = cacheHitData; // Maybe null for a miss.
            CacheMissType = cacheMissType;
        }

        /// <summary>
        /// Creates a result for a miss (or unusable descriptor)
        /// </summary>
        public static RunnableFromCacheResult CreateForMiss(WeakContentFingerprint weakFingerprint, PipCacheMissType cacheMissType)
        {
            Contract.Assert(cacheMissType != PipCacheMissType.Hit, $"Unexpected cache miss type: '{cacheMissType}'");

            return new RunnableFromCacheResult(
                weakFingerprint,
                dynamicObservations: ReadOnlyArray<(AbsolutePath, DynamicObservationKind)>.Empty,
                allowedUndeclaredSourceReads: CollectionUtilities.EmptySet<AbsolutePath>(),
                null,
                cacheMissType);
        }

        /// <summary>
        /// Creates a result for a usable descriptor.
        /// </summary>
        public static RunnableFromCacheResult CreateForHit(
            WeakContentFingerprint weakFingerprint,
            ReadOnlyArray<(AbsolutePath, DynamicObservationKind)> dynamicObservations,
            IReadOnlySet<AbsolutePath> allowedUndeclaredSourceReads,
            CacheHitData cacheHitData)
        {
            Contract.Requires(cacheHitData != null);
            return new RunnableFromCacheResult(
                weakFingerprint,
                dynamicObservations,
                allowedUndeclaredSourceReads,
                cacheHitData,
                PipCacheMissType.Hit);
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
