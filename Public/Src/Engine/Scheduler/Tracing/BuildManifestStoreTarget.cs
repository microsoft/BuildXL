// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Execution log events that need to be intercepted only on non-worker machines.
    /// </summary>
    public sealed class BuildManifestStoreTarget : ExecutionLogTargetBase
    {
        private readonly BuildManifestGenerator m_buildManifestGenerator;

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
        public BuildManifestStoreTarget(BuildManifestGenerator buildManifestGenerator)
        {
            Contract.Requires(buildManifestGenerator != null);
            m_buildManifestGenerator = buildManifestGenerator;
        }

        /// <inheritdoc/>
        public override void RecordFileForBuildManifest(RecordFileForBuildManifestEventData data)
        {
            m_buildManifestGenerator.RecordFileForBuildManifest(data.Records);
        }
    }
}
