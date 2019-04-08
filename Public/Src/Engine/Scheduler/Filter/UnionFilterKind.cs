// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler.Filter
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
