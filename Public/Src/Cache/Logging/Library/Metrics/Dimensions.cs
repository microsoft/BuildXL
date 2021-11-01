// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Logging
{
    /// <summary>
    /// Static class containing common dimension names.
    /// </summary>
    /// <remarks>
    /// Before adding a new one, make sure that it already doesn't exist here.
    /// Promote dimensions to common places as needed.
    /// RULES:
    /// (1) Don't append "Name" or "Id" to the end of the dimension (e.g. Branch instead of BranchName)
    /// (2) Use PascalCase
    /// (3) Don't use nameof to prevent renames changing the string that is sent to MDM.
    /// (4) Use the Obsolete attribute when renaming a dimension. Remove once all dashboards/alerts are migrated.
    /// (5) Create as static readonly to preserve immutability.
    /// </remarks>
    public static class Dimensions
    {
        /// <nodoc />
        public static readonly Dimension Operation = new Dimension("Operation");

        /// <nodoc />
        public static readonly Dimension Metric = new Dimension("Metric");

        /// <nodoc />
        public static readonly Dimension Component = new Dimension("Component");

        /// <nodoc />
        public static readonly Dimension OperationKind = new Dimension("OperationKind");

        /// <nodoc />
        public static readonly Dimension ExceptionType = new Dimension("ExceptionType");

        /// <summary>
        /// Whether the measured operation succeeded or not ("Succeeded" or "Failed")
        /// </summary>
        public static readonly Dimension OperationSuccess = new Dimension("OperationSuccess");

        /// <summary>
        /// The type of failure (Critical failure, Failure, etc)
        /// </summary>
        public static readonly Dimension FailureKind = new Dimension("FailureKind");
    }

    /// <summary>
    /// Represents the name of a dimension. Can only be constructed from the telemetry package to ensure
    /// all dimension names are created in one place. Using common dimension names in our telemetry will
    /// aid in DRI comprehension of metrics, help with setting common overrides for dashboards and the
    /// health model in Jarvis.
    /// </summary>
    /// <remarks>
    /// Additional functionality may be added in the future to add static typing to dimensions when logging.
    /// </remarks>
    public sealed class Dimension
    {
        /// <summary>
        /// Create a dimension.
        /// </summary>
        /// <param name="dimensionName">The name of the MDM dimension.</param>
        /// <remarks>
        /// Leave internal to keep all dimension declarations in a common place.
        /// </remarks>
        internal Dimension(string dimensionName)
        {
            Name = dimensionName;
        }

        /// <summary>
        /// The name of the Dimension in MDM.
        /// </summary>
        public string Name { get; }

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

#pragma warning disable CS1591 // disable 'Missing XML comment for publicly visible type' warnings.
    /// <summary>
    /// Represents name-value pair of a default dimension, like name: 'Machine', value: 'Machine1'.
    /// </summary>
    public record DefaultDimension(string Name, string Value)
    {
        /// <inheritdoc />
        public override string ToString()
        {
            return $"Name: {Name}, Value: {Value}";
        }
    }
#pragma warning restore CS1591
}
