// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Provides context information for directory artifacts
    /// </summary>
    /// <remarks>
    /// Used by the PipExecutor to pass directory artifact related information to (see "SandboxedProcessPipExecutor")
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

        /// <summary>
        /// Lists shared opaque directory contents and the indexes of temporary members that must be excluded from the file access manifest.
        /// </summary>
        /// <remarks>
        /// <paramref name="temporaryMemberIndexes"/> contains indexes of FileArtifacts inside of a sealed directory that do not exist on disk. The main example here
        /// are temp files under shared opaque directories -- such files were created/deleted by a pip while it was running, the files are associated
        /// with a particular DirectoryArtifact but they do not actually exist on disk.
        ///
        /// These members are omitted from explicit input entries so they do not mask write permission inherited from a shared opaque output scope.
        ///
        /// One of the reasons we need to track such files is to ensure that FileMonitoringViolationAnalyzer produces consistent results. Consider a scenario:
        /// - We are NOT tracking temp files.
        /// - PipA produces a temp file foo.bar (i.e., file exists on disk while the pip is running).
        /// - PipB probes foo.bar.
        /// - There is no dependency between PipA and PipB.
        /// - Depending on when these two pips run, the probe can either be an absent path probe or an existing path probe. If it's an existing path probe,
        ///   the analyzer will emit a DFA; otherwise, there will be no error (because absent path probes are allowed).
        ///
        /// By tracking such a temp file we ensure that there will be a DFA regardless of the order PipA and PipB were executed.
        /// </remarks>
        SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSharedOpaqueDirectoryContents(
            DirectoryArtifact directory,
            out ReadOnlyArray<int> temporaryMemberIndexes);
    }
}
