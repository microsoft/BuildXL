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
        /// Performs validations to ensure the build can safely be distributed
        /// </summary>
        bool ValidateDistribution { get; }

        /// <summary>
        /// Materialize source files on worker nodes
        /// </summary>
        bool EnableSourceFileMaterialization { get; }

        /// <summary>
        /// Materialize output files on all workers
        /// </summary>
        bool? ReplicateOutputsToWorkers { get; }

        /// <summary>
        /// Perform cache look-ups on workers
        /// </summary>
        bool DistributeCacheLookups { get; }

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
        bool FireForgetMaterializeOutput { get; }

        /// <summary>
        /// Indicates number of times the orchestrator should retry failing pips due to lost workers on a different worker.
        /// To disable feature, set EnableRetryFailedPipsOnAnotherWorker to 0.
        /// </summary>
        int? NumRetryFailedPipsOnAnotherWorker { get; }
    }
}
