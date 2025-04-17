// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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
        /// Specifies the host name where the machine running the build can be reached.
        /// By default, this is the value returned by <see cref="System.Net.Dns.GetHostName"/>
        /// but it can be overridden via the command line if needed.
        /// </summary>
        /// <remarks>
        /// This value should only be overriden by build runners, never by a user. 
        /// In particular, we need it to be overriddable because on ADO networks the machines are not reachable
        /// in the hostname that GetHostName returns, and we need a special suffix that is appended by the AdoBuildRunner.
        /// (see https://learn.microsoft.com/en-us/azure/virtual-network/virtual-networks-name-resolution-for-vms-and-role-instances) 
        /// </remarks>
        string MachineHostName { get; }

        /// <summary>
        /// The number of workers that may potentially join the build dynamically 
        /// (this should be the precise amount that we expect, but technically we only require that it is an upper bound).
        /// </summary>
        int DynamicBuildWorkerSlots { get;  }

        /// <summary>
        /// Count of workers that should be immediately released when they attempt to connect to the orchestrator.
        /// </summary>
        /// <remarks>
        /// The useful application of this feature is to do A/B testing on worker counts. Using this flag in the A/B
        /// test args will allow some portion of build traffic to run with a smaller worker count to compare at scale
        /// the effect of fewer workers. This is a wasteful option to use since the infrastructure will spin up
        /// a worker only to be immediately released. But it is much simpler to provide this functionality at
        /// the build engine level than at the infrastructure level.
        /// </remarks>
        int ImmediateWorkerRelease { get; }

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
        /// Multiplier for how aggressively to release workers. Larger values will release workers more aggressively. A value of 1
        /// will release a worker when the number of unfinished (including currently running) pips can be satisfied by the concurrency
        /// with one fewer worker.
        /// </summary>
        double EarlyWorkerReleaseMultiplier { get; }

        /// <summary>
        /// Indicates whether the orchestrator should wait for the results of materializeoutput step on remote workers.
        /// </summary>
        bool? FireForgetMaterializeOutput { get; }

        /// <summary>
        /// Indicates number of times the orchestrator should retry the pip on the remote workers due to the stopped worker, network failure, failure to send the build request, etc.
        /// </summary>
        int MaxRetryLimitOnRemoteWorkers { get; }

        /// <summary>
        /// Verify that source files that are statically declared pip inputs match between an orchestrator and a worker.
        /// </summary>
        bool VerifySourceFilesOnWorkers { get; }
    }
}
