// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines what should be included in build snapshot
    /// </summary>
    public enum SnapshotMode : byte
    {
        /// <summary>
        /// Disables snapshot
        /// </summary>
        None,

        /// <summary>
        /// Captures evaluation state in a zip file.
        /// </summary>
        Evaluation,

        /// <summary>
        /// Captures full build input state in vhd file.
        /// </summary>
        Full,
    }
}
