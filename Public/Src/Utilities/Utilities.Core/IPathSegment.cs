// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// A path segment (RelativePath or PathAtom)
    /// </summary>
    public interface IPathSegment
    {
        /// <summary>
        /// Gets the string representation of the path segment
        /// </summary>
        string ToString(StringTable stringTable, PathFormat pathFormat = PathFormat.HostOs);
    }
}
