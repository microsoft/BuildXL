// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Given multiple producers of a path (different versions), indicates if the earliest or latest matching version should be found.
    /// </summary>
    public enum VersionDisposition
    {
        /// <summary>
        /// Prefer earlier versions of the path.
        /// </summary>
        Earliest,

        /// <summary>
        /// Prefer later versions of the path.
        /// </summary>
        Latest,
    }
}
