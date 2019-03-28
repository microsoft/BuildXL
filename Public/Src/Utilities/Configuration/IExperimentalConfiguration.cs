// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Configuration settings for experimental options
    /// </summary>
    public partial interface IExperimentalConfiguration
    {
        /// <summary>
        /// Forces a contract failure which should be handled by BuildXL's unhandled exception handler. This is used
        /// for debugging crash handling
        /// </summary>
        bool ForceContractFailure { get; }

        /// <summary>
        /// Translate the subst settings to use the target file locations when calling into the cache.
        /// </summary>
        bool UseSubstTargetForCache { get; }
    }
}
