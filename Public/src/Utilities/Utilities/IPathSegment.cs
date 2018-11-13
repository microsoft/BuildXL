// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
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
