// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips
{
    /// <summary>
    /// Specifies roles that nodes can serve as in a distributed build (if applicable)
    /// </summary>
    public enum DistributedBuildRole : ushort
    {
        /// <summary>
        /// Not running distributed build.
        /// </summary>
        None = 0,

        /// <summary>
        /// Specifies that the current node acts as the build coordinator
        /// </summary>
        Master = 1,

        /// <summary>
        /// Specifies that the current node acts as a build worker
        /// </summary>
        Worker = 2,
    }
}
