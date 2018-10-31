// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Details of whether output files should be preserved
    /// </summary>
    public enum PreserveOutputsMode : byte
    {
        /// <summary>
        /// Preserving outputs is disabled. This is the normal mode of operation
        /// </summary>
        Disabled,

        /// <summary>
        /// Preserves any output files that may already exist
        /// </summary>
        Enabled,

        /// <summary>
        /// Resets the PreserveOutputs salt, which means no processes may be cache hits from a previous build where previous
        /// outputs were left on disk.
        /// </summary>
        Reset,
    }
}
