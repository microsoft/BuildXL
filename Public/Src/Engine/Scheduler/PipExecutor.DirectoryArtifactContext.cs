// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using System.Collections.Generic;

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
            public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealDirectoryContents(DirectoryArtifact directory, out IReadOnlySet<AbsolutePath> temporaryFiles)
            {
                var dirContent = m_pipExecutionEnvironment.State.FileContentManager.ListSealedDirectoryContents(directory);

                using (var pooledSet = Pools.GetAbsolutePathSet())
                {
                    var instance = pooledSet.Instance;
                    instance.UnionWith(dirContent
                        .Where(file => m_pipExecutionEnvironment.State.FileContentManager.TryGetInputContent(file, out var materializationInfo)
                            && materializationInfo.Hash == WellKnownContentHashes.AbsentFile)
                        .Select(file => file.Path));

                    temporaryFiles = instance.Count > 0
                        ? new ReadOnlyHashSet<AbsolutePath>(instance)
                        : CollectionUtilities.EmptySet<AbsolutePath>();
                }

                return dirContent;
            }
        }
    }
}
