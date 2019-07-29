// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

            IsGrpcEnabled = true;
            EarlyWorkerReleaseMultiplier = 0.5;
        }

        /// <nodoc />
        public DistributionConfiguration(IDistributionConfiguration template)
        {
            Contract.Assume(template != null);

            BuildRole = template.BuildRole;
            BuildServicePort = template.BuildServicePort;
            ValidateDistribution = template.ValidateDistribution;
            EnableSourceFileMaterialization = template.EnableSourceFileMaterialization;
            ReplicateOutputsToWorkers = template.ReplicateOutputsToWorkers;
            BuildWorkers = new List<IDistributionServiceLocation>(template.BuildWorkers.Select(location => new DistributionServiceLocation(location)));
            DistributeCacheLookups = template.DistributeCacheLookups;
            MinimumWorkers = template.MinimumWorkers;
            IsGrpcEnabled = template.IsGrpcEnabled;
            EarlyWorkerRelease = template.EarlyWorkerRelease;
            EarlyWorkerReleaseMultiplier = template.EarlyWorkerReleaseMultiplier;
        }

        /// <inhertidoc />
        public bool IsGrpcEnabled { get; set; }

        /// <inhertidoc />
        public DistributedBuildRoles BuildRole { get; set; }

        /// <inhertidoc />
        public ushort BuildServicePort { get; set; }

        /// <inhertidoc />
        public bool? ReplicateOutputsToWorkers { get; set; }

        /// <inhertidoc />
        public bool EnableSourceFileMaterialization { get; set; }

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

        /// <inheritdoc />
        public bool EarlyWorkerRelease { get; set; }

        /// <inheritdoc />
        public double EarlyWorkerReleaseMultiplier { get; set; }
    }
}
