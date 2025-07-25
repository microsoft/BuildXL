// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Class for tracking materialization of pip outputs.
    /// </summary>
    internal sealed class PipOutputMaterializationTracker
    {
        /// <summary>
        /// File content manager host.
        /// </summary>
        private readonly IFileContentManagerHost m_fileContentManagerHost;

        /// <summary>
        /// Incremental scheduling state.
        /// </summary>
        private readonly IIncrementalSchedulingState m_incrementalSchedulingState;

        /// <summary>
        /// Mappings from pips marked as clean-but-not-materialized to the number of their outputs.
        /// </summary>
        private readonly ConcurrentDictionary<PipId, int> m_nonMaterializedPips = new ConcurrentDictionary<PipId, int>();

        /// <summary>
        /// Creates an instance of <see cref="PipOutputMaterializationTracker" />.
        /// </summary>
        public PipOutputMaterializationTracker(IFileContentManagerHost fileContentManagerHost, IIncrementalSchedulingState incrementalSchedulingState)
        {
            Contract.Requires(fileContentManagerHost != null);

            m_fileContentManagerHost = fileContentManagerHost;
            m_incrementalSchedulingState = incrementalSchedulingState;
        }

        /// <summary>
        /// Adds non materialized outputs.
        /// </summary>
        public void AddNonMaterializedPip(Pip pip)
        {
            Contract.Requires(pip != null);

            int numberOfOutputs = 0;
            PipArtifacts.ForEachOutput(
                pip,
                artifact => { ++numberOfOutputs; return true; },
                getExistenceAssertionsUnderOpaqueDirectory: m_fileContentManagerHost.GetExistenceAssertionsUnderOpaqueDirectory,
                includeUncacheable: true);

            m_nonMaterializedPips.TryAdd(pip.PipId, numberOfOutputs);
        }

        /// <summary>
        /// Reports that an artifact has been materialized.
        /// </summary>
        public void ReportMaterializedArtifact(in FileOrDirectoryArtifact artifact)
        {
            var pipId = m_fileContentManagerHost.TryGetProducerId(artifact);

            if (!pipId.IsValid || !m_nonMaterializedPips.ContainsKey(pipId))
            {
                return;
            }

            m_nonMaterializedPips.AddOrUpdate(
                pipId,
                0,
                (id, i) =>
                {
                    var newI = --i;
                    if (newI == 0)
                    {
                        m_incrementalSchedulingState?.PendingUpdates.MarkNodeMaterialized(id.ToNodeId());
                    }

                    return newI;
                });
        }
    }
}
