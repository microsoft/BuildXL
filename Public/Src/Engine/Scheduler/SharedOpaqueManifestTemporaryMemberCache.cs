// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Caches the members of shared opaque directories that must be excluded from file access manifests.
    /// </summary>
    internal sealed class SharedOpaqueManifestTemporaryMemberCache
    {
        private readonly FileContentManager m_fileContentManager;
        private readonly ConcurrentBigMap<DirectoryArtifact, Lazy<TemporaryMemberIndexes>> m_temporaryMemberIndexes = new();

        public SharedOpaqueManifestTemporaryMemberCache(FileContentManager fileContentManager)
        {
            m_fileContentManager = fileContentManager;
        }

        /// <summary>
        /// Lists directory contents and returns the indexes of members whose content is absent.
        /// </summary>
        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSharedOpaqueDirectoryContents(
            DirectoryArtifact directory,
            out ReadOnlyArray<int> temporaryMemberIndexes)
        {
            var contents = m_fileContentManager.ListSealedDirectoryContents(directory);

            // A dynamic directory produced on another worker appears empty until its contents are reported.
            // Avoid caching that provisional result; rescanning an actually empty directory is trivial.
            if (contents.Length == 0)
            {
                temporaryMemberIndexes = ReadOnlyArray<int>.Empty;
                return contents;
            }

            // Publish the Lazy under the map lock, then let concurrent callers share the classification outside that lock.
            var temporaryMemberIndexesEntry = m_temporaryMemberIndexes.GetOrAdd(
                directory,
                contents,
                (_, directoryContents) => Lazy.Create(
                    () => FindTemporaryMemberIndexes(directoryContents)));
            var lazyTemporaryMemberIndexes = temporaryMemberIndexesEntry.Item.Value;
            var result = lazyTemporaryMemberIndexes.Value;
            temporaryMemberIndexes = result.Indexes;

            if (!result.AllContentKnown)
            {
                m_temporaryMemberIndexes.CompareRemove(directory, lazyTemporaryMemberIndexes);
            }

            return contents;
        }

        private TemporaryMemberIndexes FindTemporaryMemberIndexes(
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> contents)
        {
            List<int> indexes = null;
            bool allContentKnown = true;
            for (int i = 0; i < contents.Length; i++)
            {
                if (!m_fileContentManager.TryGetInputContent(contents[i], out var materializationInfo))
                {
                    allContentKnown = false;
                }
                else if (materializationInfo.Hash == WellKnownContentHashes.AbsentFile)
                {
                    indexes ??= new List<int>();
                    indexes.Add(i);
                }
            }

            var temporaryMemberIndexes = indexes == null
                ? ReadOnlyArray<int>.Empty
                : ReadOnlyArray<int>.FromWithoutCopy(indexes.ToArray());

            return new TemporaryMemberIndexes(temporaryMemberIndexes, allContentKnown);
        }

        private readonly struct TemporaryMemberIndexes
        {
            public ReadOnlyArray<int> Indexes { get; }

            /// <summary>
            /// Whether materialization information was available for every directory member.
            /// Production scheduling is expected to make this information available before a consuming pip runs.
            /// The prior uncached implementation nevertheless tolerated missing information and retried classification
            /// on every call, so handle this gracefully by tracking and not caching these cases (if they ever come up)
            /// </summary>
            public bool AllContentKnown { get; }

            public TemporaryMemberIndexes(ReadOnlyArray<int> indexes, bool allContentKnown)
            {
                Indexes = indexes;
                AllContentKnown = allContentKnown;
            }
        }
    }
}
