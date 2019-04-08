// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Specifies the set of outputs which must be materialized
    /// </summary>
    public enum RequiredOutputMaterialization : byte
    {
        /// <summary>
        /// All outputs are required
        /// </summary>
        All,

        /// <summary>
        /// Requires outputs for explicitly scheduled nodes
        /// </summary>
        Explicit,

        /// <summary>
        /// No outputs are required
        /// </summary>
        Minimal,
    }
}
