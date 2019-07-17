// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// How ServerMode should operate
    /// </summary>
    public enum ServerMode : byte
    {
        /// <summary>
        /// ServerMode is disabled.
        /// </summary>
        Disabled,

        /// <summary>
        /// Preserves any output files that may already exist
        /// </summary>
        Enabled,

        /// <summary>
        /// Kills the server process associated with this client. Does not perform a build
        /// </summary>
        Kill,
    }
}
