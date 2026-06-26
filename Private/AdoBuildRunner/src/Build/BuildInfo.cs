// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using System;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// A build context represents an ongoing VSTS build and its most important properties
    /// </summary>
    public record class BuildInfo
    {
        /// <nodoc />
        public required string RelatedSessionId { get; init; }

        /// <summary>
        /// On a distributed build, a worker build triggered by the AdoBuildRunner
        /// will hold the GRPC endpoint to communicate with the orchestrator on this field,
        /// which will be null otherwise
        /// </summary>
        public required string OrchestratorLocation { get; init; }

        /// <summary>
        /// Orchestrator 
        /// </summary>
        public required string OrchestratorPool { get; init; }

        /// <summary>
        /// The ADO timeline JobId of the orchestrator's job (matches the orch's
        /// <c>SYSTEM_JOBID</c> environment variable). The worker uses this to locate the
        /// orchestrator's record in the orch build's timeline and observe ORCHESTRATOR JOB
        /// state, rather than build-level metadata. Empty string when not published (treated
        /// as "monitor disabled" by the worker).
        /// </summary>
        public string OrchestratorJobId { get; init; } = string.Empty;

        /// <nodoc />
        public string Serialize() => $"{RelatedSessionId};{OrchestratorLocation};{OrchestratorPool};{OrchestratorJobId}";

        /// <nodoc />
        public static BuildInfo Deserialize(string serialized)
        {
            var splits = serialized.Split(';');
            if (splits.Length != 3 && splits.Length != 4)
            {
                throw new ArgumentException("The provided string does not represent a valid BuildInfo");
            }

            return new BuildInfo
            {
                RelatedSessionId = splits[0],
                OrchestratorLocation = splits[1],
                OrchestratorPool = splits[2],
                OrchestratorJobId = splits.Length == 4 ? splits[3] : string.Empty,
            };
        }
    }
}
