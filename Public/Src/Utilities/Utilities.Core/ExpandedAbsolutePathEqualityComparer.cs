// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Class for doing equality comparisons on <see cref="ExpandedAbsolutePath"/> when the associated <see cref="PathTable"/> is guaranteed to be
    /// always the same
    /// </summary>
    /// <remarks>
    /// The comparisons are done based on the <see cref="ExpandedAbsolutePath.Path"/> directly, since
    /// it should always be in sync with <see cref="ExpandedAbsolutePath.ExpandedPath"/>
    /// </remarks>
    public sealed class ExpandedAbsolutePathEqualityComparer : IEqualityComparer<ExpandedAbsolutePath>
    {
        /// <nodoc/>
        public static ExpandedAbsolutePathEqualityComparer Instance = new ExpandedAbsolutePathEqualityComparer();

        /// <inheritdoc/>
        public bool Equals(ExpandedAbsolutePath left, ExpandedAbsolutePath right)
        {
            return left.Path.Equals(right.Path);
        }

        /// <inheritdoc/>
        public int GetHashCode(ExpandedAbsolutePath expandedAbsolutePath)
        {
            return expandedAbsolutePath.Path.GetHashCode();
        }
    }
}
