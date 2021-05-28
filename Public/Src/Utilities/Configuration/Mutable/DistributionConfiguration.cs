// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class DistributionConfiguration : IDistributionConfiguration
    {
        /// <nodoc />
        public DistributionConfiguration()
        {
            BuildWorkers = new List<IDistributionServiceLocation>();

            // Local worker is always connected.
            MinimumWorkers = 1;
            EarlyWorkerReleaseMultiplier = 0.5;
            EarlyWorkerRelease = true;
        }

        /// <nodoc />
        public DistributionConfiguration(IDistributionConfiguration template)
        {
            Contract.Assume(template != null);

            BuildRole = template.BuildRole;
            BuildServicePort = template.BuildServicePort;
            ValidateDistribution = template.ValidateDistribution;
            ReplicateOutputsToWorkers = template.ReplicateOutputsToWorkers;
            BuildWorkers = new List<IDistributionServiceLocation>(template.BuildWorkers.Select(location => new DistributionServiceLocation(location)));
            DistributeCacheLookups = template.DistributeCacheLookups;
            MinimumWorkers = template.MinimumWorkers;
            LowWorkersWarningThreshold = template.LowWorkersWarningThreshold;
            EarlyWorkerRelease = template.EarlyWorkerRelease;
            EarlyWorkerReleaseMultiplier = template.EarlyWorkerReleaseMultiplier;
            FireForgetMaterializeOutput = template.FireForgetMaterializeOutput;
            NumRetryFailedPipsOnAnotherWorker = template.NumRetryFailedPipsOnAnotherWorker;
        }

        /// <inhertidoc />
        public DistributedBuildRoles BuildRole { get; set; }

        /// <inhertidoc />
        public ushort BuildServicePort { get; set; }

        /// <inhertidoc />
        public bool? ReplicateOutputsToWorkers { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IDistributionServiceLocation> BuildWorkers { get; set; }

        /// <inhertidoc />
        public bool ValidateDistribution { get; set; }

        /// <inhertidoc />
        IReadOnlyList<IDistributionServiceLocation> IDistributionConfiguration.BuildWorkers => BuildWorkers;

        /// <inhertidoc />
        public bool DistributeCacheLookups { get; set; }

        /// <inhertidoc />
        public int MinimumWorkers { get; set; }

        /// <inhertidoc />
        public int? LowWorkersWarningThreshold { get; set; }

        /// <inheritdoc />
        public bool EarlyWorkerRelease { get; set; }

        /// <inheritdoc />
        public double EarlyWorkerReleaseMultiplier { get; set; }

        /// <inheritdoc />
        public bool FireForgetMaterializeOutput { get; set; }

        /// <inheritdoc />
        public int? NumRetryFailedPipsOnAnotherWorker { get; set; }
    }
}
