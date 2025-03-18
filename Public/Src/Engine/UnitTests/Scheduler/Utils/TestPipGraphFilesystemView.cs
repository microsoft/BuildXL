// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace Test.BuildXL.Scheduler
{
    public class TestPipGraphFilesystemView : IPipGraphFileSystemView
    {
        private readonly PathTable m_pathTable;

        public TestPipGraphFilesystemView(PathTable pathTable)
        {
            m_pathTable = pathTable;
        }

        public IReadOnlySet<FileArtifact> GetExistenceAssertionsUnderOpaqueDirectory(DirectoryArtifact directoryArtifact) => CollectionUtilities.EmptySet<FileArtifact>();

        public bool IsPathUnderOutputDirectory(AbsolutePath path, out bool isItUnderSharedOpaque)
        {
            isItUnderSharedOpaque = false;
            return false;
        }

        public FileArtifact TryGetLatestFileArtifactForPath(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            return FileArtifact.Invalid;
        }

        public Optional<(AbsolutePath path, bool isShared)> TryGetParentOutputDirectory(AbsolutePath path)
        {
            return default;
        }
    }
}