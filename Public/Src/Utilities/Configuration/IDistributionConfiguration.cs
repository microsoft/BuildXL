// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Configuration for distribution
    /// </summary>
    public interface IDistributionConfiguration
    {
        /// <summary>,
        /// Is Grpc enabled.
        /// </summary>
        bool IsGrpcEnabled { get; }

        /// <summary>
        /// Specifies the roles the node plays in the distributed build {get;} or [W]orker. This argument is required for executing a distributed build. (short form: /dbr)
        /// </summary>
        DistributedBuildRoles BuildRole { get; }

        /// <summary>
        /// Specifies the TCP port of a locally running distributed build service (master or worker) which peers can connect to during a distributed build. This argument is required for
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
        /// Indicates whether the remote workers should be released early in case of insufficient amount of work. 
        /// </summary>
        bool EarlyWorkerRelease { get; }

        /// <summary>
        /// Specifies the capacity multiplier when we start releasing the workers.
        /// </summary>
        double EarlyWorkerReleaseMultiplier { get; }
    }
}
