// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Configuration object for automatic (instrumented) deserialization of reclassification rules
    /// </summary>
    public class ReclassificationRuleConfig : TrackedValue, IReclassificationRuleConfig
    {
        /// <nodoc />
        public ReclassificationRuleConfig()
        {
            ResolvedObservationTypes = new List<ObservationType>();
        }

        /// <nodoc />
        public ReclassificationRuleConfig(IReclassificationRuleConfig template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            Name = template.Name;
            ResolvedObservationTypes = new List<ObservationType>(template.ResolvedObservationTypes);
            ReclassifyTo = template.ReclassifyTo;
            PathRegex = template.PathRegex;
        }

        /// <inheritdoc />
        public string Name { get; set; }

        /// <inheritdoc />
        public string PathRegex { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<ObservationType> ResolvedObservationTypes { get; set; }

        /// <inheritdoc />
        public DiscriminatingUnion<ObservationType,UnitValue> ReclassifyTo { get; set; }

        /// <inheritdoc />
        public IReclassificationRule GetRule()
        {
            return new ReclassificationRule()
            {
                Name = Name,
                PathRegex = PathRegex,
                ResolvedObservationTypes = ResolvedObservationTypes,
                ReclassifyTo = ReclassifyTo
            };
        }
    }
}
