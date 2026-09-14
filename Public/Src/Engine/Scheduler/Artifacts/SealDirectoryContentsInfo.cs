// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Pips.Artifacts;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler.Artifacts
{
    /// <summary>
    /// The sorted contents of a sealed directory together with its temporary members.
    /// </summary>
    internal readonly struct SealDirectoryContentsInfo
    {
        private static readonly FileArtifactWithAttributesOrdinalComparer s_fileArtifactWithAttributesOrdinalComparer = new();

        /// <summary>
        /// Gets an empty directory contents value.
        /// </summary>
        public static SealDirectoryContentsInfo Empty { get; } = new SealDirectoryContentsInfo(
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(
                Array.Empty<FileArtifact>(),
                OrdinalFileArtifactComparer.Instance),
            ReadOnlyArray<int>.Empty);

        private SealDirectoryContentsInfo(
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> contents,
            ReadOnlyArray<int> temporaryMemberIndexes)
        {
            Contents = contents;
            TemporaryMemberIndexes = temporaryMemberIndexes;
        }

        /// <summary>
        /// Creates directory contents from members carrying their existence attributes.
        /// </summary>
        public static SealDirectoryContentsInfo Create(List<FileArtifactWithAttributes> contents)
        {
            if (contents.Count == 0)
            {
                return Empty;
            }

            contents.Sort(s_fileArtifactWithAttributesOrdinalComparer);

            var artifacts = new FileArtifact[contents.Count];
            using (var temporaryMemberIndexesWrapper = Pools.GetIntList())
            {
                var temporaryMemberIndexes = temporaryMemberIndexesWrapper.Instance;
                for (int i = 0; i < contents.Count; i++)
                {
                    var member = contents[i];
                    artifacts[i] = member.ToFileArtifact();

                    if (member.FileExistence == FileExistence.Temporary)
                    {
                        temporaryMemberIndexes.Add(i);
                    }
                }

                return new SealDirectoryContentsInfo(
                    SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.FromSortedArrayUnsafe(
                        ReadOnlyArray<FileArtifact>.FromWithoutCopy(artifacts),
                        OrdinalFileArtifactComparer.Instance),
                    temporaryMemberIndexes.Count == 0
                        ? ReadOnlyArray<int>.Empty
                        : ReadOnlyArray<int>.FromWithoutCopy(temporaryMemberIndexes.ToArray()));
            }
        }

        /// <summary>
        /// Creates directory contents from an already sorted member collection with no temporary members.
        /// </summary>
        public static SealDirectoryContentsInfo Create(
            SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> contents)
        {
            return contents.Length == 0
                ? Empty
                : new SealDirectoryContentsInfo(contents, ReadOnlyArray<int>.Empty);
        }

        /// <summary>
        /// Gets the directory members sorted by <see cref="OrdinalFileArtifactComparer"/>.
        /// </summary>
        /// <remarks>
        /// Sealed directory members have historically been sorted. This supports binary-search-based access and keeps
        /// path IDs close together for delta encoding.
        /// </remarks>
        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> Contents { get; }

        /// <summary>
        /// Gets the sorted indexes into <see cref="Contents"/> for members with <see cref="FileExistence.Temporary"/>.
        /// </summary>
        public ReadOnlyArray<int> TemporaryMemberIndexes { get; }

        /// <summary>
        /// Returns an allocation-free enumerator over the directory members and their temporary status.
        /// </summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(Contents, TemporaryMemberIndexes);
        }

        /// <summary>
        /// A directory member together with its temporary status.
        /// </summary>
        public readonly struct Member
        {
            /// <summary>
            /// Creates a directory member.
            /// </summary>
            public Member(FileArtifact artifact, bool isTemporary)
            {
                Artifact = artifact;
                IsTemporary = isTemporary;
            }

            /// <summary>
            /// Gets the file artifact.
            /// </summary>
            public FileArtifact Artifact { get; }

            /// <summary>
            /// Gets whether the member is temporary.
            /// </summary>
            public bool IsTemporary { get; }
        }

        /// <summary>
        /// Enumerates directory members while advancing through the compact temporary-member index list.
        /// </summary>
        public struct Enumerator
        {
            private readonly SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> m_contents;
            private readonly ReadOnlyArray<int> m_temporaryMemberIndexes;
            private int m_contentIndex;
            private int m_temporaryMemberIndex;

            internal Enumerator(
                SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> contents,
                ReadOnlyArray<int> temporaryMemberIndexes)
            {
                m_contents = contents;
                m_temporaryMemberIndexes = temporaryMemberIndexes;
                m_contentIndex = -1;
                m_temporaryMemberIndex = 0;
                Current = default;
            }

            /// <summary>
            /// Gets the current directory member.
            /// </summary>
            public Member Current { get; private set; }

            /// <summary>
            /// Advances to the next directory member.
            /// </summary>
            public bool MoveNext()
            {
                m_contentIndex++;
                if (m_contentIndex >= m_contents.Length)
                {
                    return false;
                }

                bool isTemporary = m_temporaryMemberIndex < m_temporaryMemberIndexes.Length
                    && m_temporaryMemberIndexes[m_temporaryMemberIndex] == m_contentIndex;
                if (isTemporary)
                {
                    m_temporaryMemberIndex++;
                }

                Current = new Member(m_contents[m_contentIndex], isTemporary);
                return true;
            }
        }

        private sealed class FileArtifactWithAttributesOrdinalComparer : IComparer<FileArtifactWithAttributes>
        {
            public int Compare(FileArtifactWithAttributes x, FileArtifactWithAttributes y)
            {
                return OrdinalFileArtifactComparer.Instance.Compare(x.ToFileArtifact(), y.ToFileArtifact());
            }
        }
    }
}
