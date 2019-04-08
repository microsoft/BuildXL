// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class ExperimentalConfiguration : IExperimentalConfiguration
    {
        /// <nodoc />
        public ExperimentalConfiguration()
        {
        }

        /// <nodoc />
        public ExperimentalConfiguration(IExperimentalConfiguration template)
        {
            Contract.Assume(template != null);

            ForceContractFailure = template.ForceContractFailure;
            UseSubstTargetForCache = template.UseSubstTargetForCache;
        }

        /// <inhertidoc />
        public bool ForceContractFailure { get; set; }

        /// <inhertidoc />
        public bool UseSubstTargetForCache { get; set; }
    }
}
