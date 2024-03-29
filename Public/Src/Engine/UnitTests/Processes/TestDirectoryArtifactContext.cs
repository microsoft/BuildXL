// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.ProcessPipExecutor;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// A context for directory artifacts for testing purposes only
    /// </summary>
    public sealed class TestDirectoryArtifactContext : IDirectoryArtifactContext
    {
        private readonly SealDirectoryKind m_fakeKind;
        private readonly IEnumerable<FileArtifact> m_inputContent;

        /// <summary>
        /// Returns and 'empty' context, where all directories are seens as <see cref="SealDirectoryKind.Full"/> with empty content
        /// </summary>
        public static TestDirectoryArtifactContext Empty = new TestDirectoryArtifactContext(SealDirectoryKind.Full, CollectionUtilities.EmptyArray<FileArtifact>());

        /// <summary>
        /// Returns <see cref="SealDirectoryKind.Full"/> and same <paramref name="fakeInputContent"/> for all directories
        /// </summary>
        public TestDirectoryArtifactContext(IEnumerable<FileArtifact> fakeInputContent) : this(SealDirectoryKind.Full, fakeInputContent)
        { }

        /// <summary>
        /// Returns the same <paramref name="fakeKind"/> and same <paramref name="fakeInputContent"/> for all directories
        /// </summary>
        public TestDirectoryArtifactContext(SealDirectoryKind fakeKind, IEnumerable<FileArtifact> fakeInputContent)
        {
            m_fakeKind = fakeKind;
            m_inputContent = fakeInputContent;
        }

        /// <inheritdoc/>
        public SealDirectoryKind GetSealDirectoryKind(DirectoryArtifact directoryArtifact)
        {
            return m_fakeKind;
        }

        /// <inheritdoc/>
        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealDirectoryContents(DirectoryArtifact directory, out IReadOnlySet<AbsolutePath> temporaryFiles)
        {
            temporaryFiles = CollectionUtilities.EmptySet<AbsolutePath>();
            return SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(m_inputContent, OrdinalFileArtifactComparer.Instance);
        }
    }
}
