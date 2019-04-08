// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines the mode of forceSkipDeps (aka dirty build)
    /// </summary>
    public enum ForceSkipDependenciesMode : byte
    {
        /// <summary>
        /// ForceSkipDeps is disabled
        /// </summary>
        Disabled,

        /// <summary>
        /// Dependencies are skipped if the inputs are present
        /// </summary>
        Always,

        /// <summary>
        /// Dependencies are skipped if they are from other modules and the inputs are present
        /// </summary>
        Module,
    }
}
