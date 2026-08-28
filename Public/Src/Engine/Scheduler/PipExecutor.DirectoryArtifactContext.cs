// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.ProcessPipExecutor;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler
{
    public static partial class PipExecutor
    {
        /// <summary>
        /// Directory-related information to be passed to <see cref="ProcessPipExecutor.SandboxedProcessPipExecutor"/>
        /// </summary>
        private sealed class DirectoryArtifactContext : IDirectoryArtifactContext
        {
            private readonly IPipExecutionEnvironment m_pipExecutionEnvironment;

            /// <nodoc/>
            public DirectoryArtifactContext(IPipExecutionEnvironment pipExecutionEnvironment)
            {
                Contract.Requires(pipExecutionEnvironment != null);

                m_pipExecutionEnvironment = pipExecutionEnvironment;
            }

            /// <inheritdoc/>
            public SealDirectoryKind GetSealDirectoryKind(DirectoryArtifact directoryArtifact)
            {
                return m_pipExecutionEnvironment.GetSealDirectoryKind(directoryArtifact);
            }

            /// <inheritdoc/>
            public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealDirectoryContents(DirectoryArtifact directory)
            {
                return m_pipExecutionEnvironment.State.FileContentManager.ListSealedDirectoryContents(directory);
            }

            /// <inheritdoc/>
            public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSharedOpaqueDirectoryContents(
                DirectoryArtifact directory,
                out ReadOnlyArray<int> temporaryMemberIndexes)
            {
                return m_pipExecutionEnvironment.State.SharedOpaqueManifestTemporaryMemberCache.ListSharedOpaqueDirectoryContents(
                    directory,
                    out temporaryMemberIndexes);
            }
        }
    }
}
