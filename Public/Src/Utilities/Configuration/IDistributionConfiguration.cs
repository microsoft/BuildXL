// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Configuration for distribution
    /// </summary>
    public interface IDistributionConfiguration
    {
        /// <summary>
        /// Specifies the roles the node plays in the distributed build: None, Orchestrator or Worker. This argument is required for executing a distributed build. (short form: /dbr)
        /// </summary>
        DistributedBuildRoles BuildRole { get; }

        /// <summary>
        /// Specifies the TCP port of a locally running distributed build service (orchestrator or worker) which peers can connect to during a distributed build. This argument is required for
        /// executing a distributed build.  (short form: /dbsp)
        /// </summary>
        ushort BuildServicePort { get; }

        /// <summary>
        /// Specifies the IP address or host name and TCP port of remote worker build services which this process can dispatch work to during a distributed build (can specify multiple).
        /// (short form: /dbw)
        /// </summary>
        [NotNull]
        IReadOnlyList<IDistributionServiceLocation> BuildWorkers { get; }
        
        /// <summary>
        /// Specifies the IP address or host name and TCP port of the orchestrator
        /// (short form: /dbo)
        /// </summary>
        /// <remarks>Can be null (in non-dynamic workers)</remarks>
        IDistributionServiceLocation OrchestratorLocation { get; }

        /// <summary>
        /// The number of workers that may potentially join the build dynamically 
        /// (this should be the precise amount that we expect, but technically we only require that it is an upper bound).
        /// </summary>
        int DynamicBuildWorkerSlots { get;  }

        /// <summary>
        /// The total number of remote workers that we expect for this build, i.e. the sum of DynamicBuildWorkerSlots
        /// and the number of BuildWorkers known at the start of the build
        /// </summary>
        int RemoteWorkerCount { get; }

        /// <summary>
        /// Performs validations to ensure the build can safely be distributed
        /// </summary>
        bool ValidateDistribution { get; }

        /// <summary>
        /// Materialize output files on all workers
        /// </summary>
        bool? ReplicateOutputsToWorkers { get; }

        /// <summary>
        /// Minimum number of workers that BuildXL needs to connect within a fixed time; otherwise BuildXL will fail.
        /// </summary>
        int MinimumWorkers { get; }

        /// <summary>
        /// Minimum number of workers that BuildXL needs to connect within a fixed time; otherwise BuildXL will issue a warning.
        /// </summary>
        int? LowWorkersWarningThreshold { get; }

        /// <summary>
        /// Indicates whether the remote workers should be released early in case of insufficient amount of work.
        /// </summary>
        bool EarlyWorkerRelease { get; }

        /// <summary>
        /// Specifies the capacity multiplier when we start releasing the workers.
        /// </summary>
        double EarlyWorkerReleaseMultiplier { get; }

        /// <summary>
        /// Indicates whether the orchestrator should wait for the results of materializeoutput step on remote workers.
        /// </summary>
        bool? FireForgetMaterializeOutput { get; }

        /// <summary>
        /// Indicates number of times the orchestrator should retry failing pips due to lost workers on a different worker.
        /// To disable feature, set EnableRetryFailedPipsOnAnotherWorker to 0.
        /// </summary>
        int? NumRetryFailedPipsOnAnotherWorker { get; }


        /// <summary>
        /// Verify that source files that are statically declared pip inputs match between an orchestrator and a worker.
        /// </summary>
        bool VerifySourceFilesOnWorkers { get; }
    }
}
