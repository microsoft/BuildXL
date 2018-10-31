// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Specifies existence attributes for the file output dependency
    /// </summary>
    public enum FileExistence : byte
    {
        /// <summary>
        /// Regular (required) output dependency.
        /// </summary>
        /// <remarks>
        /// Output dependency that could be used as an inputs for other pips.
        /// </remarks>
        Required = 0,

        /// <summary>
        /// Output dependency is optional (i.e., temporary).
        /// </summary>
        /// <remarks>
        /// Pip should not use temporary artifacts as an input.
        /// This type of output is very similar to pure temporary output with one exception: all other validation rules
        /// area applied for Temporary files like double writes etc.
        /// </remarks>
        Temporary = 1,

        /// <summary>
        /// Optional output dependency
        /// </summary>
        /// <remarks>
        /// Optional output dependencies can be used as inputs to other pips, but file presence is not verified after the tool runs,
        /// which means that tools taking a dependency on optional outputs have to deal with absent files
        /// </remarks>
        Optional = 2,
    }
}
