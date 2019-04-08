// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Options for caching values
    /// </summary>
    public enum SpecCachingOption
    {
        /// <summary>
        /// The system will try to pick the best strategy for you
        /// </summary>
        Auto,

        /// <summary>
        /// Caching is enabled
        /// </summary>
        Enabled,

        /// <summary>
        /// Caching is disabled
        /// </summary>
        Disabled,
    }
}
