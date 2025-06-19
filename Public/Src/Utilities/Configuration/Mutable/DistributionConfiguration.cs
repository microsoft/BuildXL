// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class DistributionConfiguration : IDistributionConfiguration
    {
        /// <nodoc />
        public DistributionConfiguration()
        {
            BuildWorkers = new List<IDistributionServiceLocation>();
            MachineHostName = Dns.GetHostName();

            // Local worker is always connected.
            MinimumWorkers = 1;
            EarlyWorkerReleaseMultiplier = 2;
            EarlyWorkerRelease = true;
            VerifySourceFilesOnWorkers = false; // TODO: For testing purposes, this is going to be disabled by default. Update in the future to be enabled by default
            MaxRetryLimitOnRemoteWorkers = 3;
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
            DynamicBuildWorkerSlots = template.DynamicBuildWorkerSlots;
            ImmediateWorkerRelease = template.ImmediateWorkerRelease;
            OrchestratorLocation = template.OrchestratorLocation;
            MachineHostName = template.MachineHostName;
            MinimumWorkers = template.MinimumWorkers;
            LowWorkersWarningThreshold = template.LowWorkersWarningThreshold;
            EarlyWorkerRelease = template.EarlyWorkerRelease;
            EarlyWorkerReleaseMultiplier = template.EarlyWorkerReleaseMultiplier;
            MaxRetryLimitOnRemoteWorkers = template.MaxRetryLimitOnRemoteWorkers;
            VerifySourceFilesOnWorkers = template.VerifySourceFilesOnWorkers;
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

        /// <nodoc />
        public IDistributionServiceLocation OrchestratorLocation { get; set; }

        /// <nodoc />
        public string MachineHostName { get; set; }

        /// <inheritdoc />
        public int DynamicBuildWorkerSlots { get; set; }

        /// <inheritdoc />
        public int ImmediateWorkerRelease { get; set; }

        /// <inheritdoc />
        public int RemoteWorkerCount => BuildWorkers.Count + DynamicBuildWorkerSlots;

        /// <inhertidoc />
        public bool ValidateDistribution { get; set; }

        /// <inhertidoc />
        IReadOnlyList<IDistributionServiceLocation> IDistributionConfiguration.BuildWorkers => BuildWorkers;

        /// <inhertidoc />
        public int MinimumWorkers { get; set; }

        /// <inhertidoc />
        public int? LowWorkersWarningThreshold { get; set; }

        /// <inheritdoc />
        public bool EarlyWorkerRelease { get; set; }

        /// <inheritdoc />
        public double EarlyWorkerReleaseMultiplier { get; set; }

        /// <inheritdoc />
        public int MaxRetryLimitOnRemoteWorkers { get; set; }

        /// <inheritdoc />
        public bool VerifySourceFilesOnWorkers { get; set; }
    }
}
