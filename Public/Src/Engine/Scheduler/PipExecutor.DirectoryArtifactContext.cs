// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler
{
    public static partial class PipExecutor
    {
        /// <summary>
        /// Directory-related information to be passed to <see cref="SandboxedProcessPipExecutor"/>
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
        }
    }
}
