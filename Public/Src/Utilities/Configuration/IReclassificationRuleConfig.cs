// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Data fields of a reclassification rule
    /// </summary>
    /// <remarks>
    /// This weird interface structure is to be able to share this definition between the configuration data
    /// (<see cref="IReclassificationRuleConfig"/>) we use in the instrumented configuration parsing and the 
    /// concrete object (with extra behavior) we use throughout the engine (<see cref="IReclassificationRule"/>)
    /// </remarks>
    public interface IReclassificationRuleData
    {
        /// <summary>
        /// An optional name, for display purposes
        /// </summary>
        /// <remarks>
        /// Can be null
        /// </remarks>
        string Name { get; }

        /// <summary>
        /// Pattern to match against accessed paths. Mandatory
        /// </summary>
        string PathRegex { get; }

        /// <summary>
        /// The rule matches if the observation is resolved to any of these types
        /// If this field is not, this rule will match against any type.
        /// </summary>
        IReadOnlyList<ObservationType> ResolvedObservationTypes { get; }

        /// <summary>
        /// When this rule applies, the observation is reclassified to this.
        /// A value of UnitValue indicates the observation should be ignored,
        /// and leaving this undefined will make the reclassification the identity
        /// </summary>
        DiscriminatingUnion<ObservationType, UnitValue> ReclassifyTo { get; }
    }

    /// <summary>
    /// Interface to use as the configuration type for automatic parsing of a <see cref="IReclassificationRule"/> from DScript
    /// </summary>
    public interface IReclassificationRuleConfig : IReclassificationRuleData, ITrackedValue
    {
        /// <summary>
        /// Get the concrete rule from the configuration object
        /// </summary>
        IReclassificationRule GetRule();
    }
}