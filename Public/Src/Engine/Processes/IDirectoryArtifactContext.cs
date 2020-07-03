// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Processes
{
    /// <summary>
    /// Provides context information for directory artifacts
    /// </summary>
    /// <remarks>
    /// Used by the PipExecutor to pass directory artifact related information to <see cref="SandboxedProcessPipExecutor"/>
    /// </remarks>
    public interface IDirectoryArtifactContext
    {
        /// <summary>
        /// Returns the directory kind of the given directory artifact
        /// </summary>
        SealDirectoryKind GetSealDirectoryKind(DirectoryArtifact directoryArtifact);

        /// <summary>
        /// Lists the contents of a sealed directory (static or dynamic).
        /// </summary>
        SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealDirectoryContents(DirectoryArtifact directory);
    }
}
