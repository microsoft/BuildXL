// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips.Filter
{
    /// <summary>
    /// Defines the kinds of combinable union filters.
    /// </summary>
    public enum UnionFilterKind
    {
        /// <summary>
        /// Filter is not combinable
        /// </summary>
        None,

        /// <summary>
        /// Tags filter (Filter must be <see cref="TagFilter"/> or <see cref="MultiTagsOrFilter"/>)
        /// </summary>
        Tags,

        /// <summary>
        /// Modules filter (Filter must be <see cref="ModuleFilter"/>)
        /// </summary>
        Modules
    }
}
