// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Scheduler.Cache;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Execution log events that need to be intercepted only on non-worker machines.
    /// </summary>
    public sealed class BuildManifestStoreTarget : ExecutionLogTargetBase
    {
        private readonly BuildManifestGenerator m_buildManifestGenerator;
        private readonly PipTwoPhaseCache m_pipTwoPhaseCache;

        /// <summary>
        /// Handle the events from workers
        /// </summary>
        public override bool CanHandleWorkerEvents => true;

        /// <summary>
        /// Handle the events from workers
        /// </summary>
        public override IExecutionLogTarget CreateWorkerTarget(uint workerId) => this;

        /// <summary>
        /// Constructor.
        /// </summary>
        public BuildManifestStoreTarget(BuildManifestGenerator buildManifestGenerator, PipTwoPhaseCache pipTwoPhaseCache)
        {
            Contract.Requires(buildManifestGenerator != null);
            m_buildManifestGenerator = buildManifestGenerator;
            m_pipTwoPhaseCache = pipTwoPhaseCache;
        }

        /// <inheritdoc/>
        public override void RecordFileForBuildManifest(RecordFileForBuildManifestEventData data)
        {
            m_buildManifestGenerator.RecordFileForBuildManifest(data.Records);
            
            // Need to update the HistoricMetadataCache on the orchestrator (only orchestrator uploads the db to cache)
            foreach (var entry in data.Records)
            {
                foreach (var manifestHash in entry.BuildManifestHashes)
                {
                    m_pipTwoPhaseCache.TryStoreBuildManifestHash(entry.AzureArtifactsHash, manifestHash);
                }
            }
        }
    }
}
