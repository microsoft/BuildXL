// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Reclassification
{
    /// <summary>
    /// Represents the result of a reclassification attempt.
    /// </summary>
    /// <remarks>
    /// Contains information about the rule that was applied, the type it was reclassified to, and the new path.
    /// If the observation type is null, it indicates that the original access needs to be ignored.
    /// </remarks>
    public readonly record struct ReclassificationResult(string AppliedRuleName, ObservationType? ReclassifyToType, AbsolutePath ReclassifyToPath);

    /// <summary>
    /// An abstraction for rules that can reclassify observed file accesses.
    /// </summary>
    /// <remarks>
    /// There may be multiple types of rules implementing this interface, each with its own logic for reclassification. This interface allows ObservedInputProcessor
    /// and the ObservationReclassifier to work with any rule type uniformly.
    /// </remarks>
    public interface IInternalReclassificationRule
    {
        /// <nodoc/>
        string Name();

        /// <summary>
        /// A unique descriptor for this rule, used for fingerprinting
        /// </summary>
        /// <remarks>
        /// If any rule changes its behavior, this descriptor must also change to ensure proper cache invalidation.
        /// </remarks>
        string Descriptor();

        /// <nodoc/>
        bool Validate(out string error);

        /// <summary>
        /// Tries to reclassify the given path from its observed type to another type/path.
        /// </summary>
        /// <remarks>
        /// If the reclassification is successful, the new type/path will be returned in the reclassification result.
        /// </remarks>
        bool TryReclassify(ExpandedAbsolutePath path, PathTable pathTable, ObservationType type, out ReclassificationResult reclassification);

        /// <summary>
        /// A dictionary representation of the rule for display purposes.
        /// </summary>
        /// <remarks>
        /// Typically used for displaying the rules in analyzers
        /// </remarks>
        IDictionary<string, object> GetDisplayDescription(PathTable pathTable);
    }
}
