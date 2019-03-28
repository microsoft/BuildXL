// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Analyzer
{
    /// <summary>
    /// Configuration object that controls the workspace construction.
    /// </summary>
    public sealed class WorkspaceBuilderConfiguration
    {
        /// <summary>
        /// Whether to cancel on first failure when building the workspace or continue as far as possible
        /// </summary>
        public bool CancelOnFirstParsingFailure { get; set; } = true;

        /// <summary>
        /// Whether to use this optimization. Off by default.
        /// </summary>
        public bool PublicFacadeOptimization { get; set; } = false;

        /// <summary>
        /// Whether to save the binding fingerprint. Off by default.
        /// </summary>
        public bool SaveBindingFingerprint { get; set; } = false;

        /// <summary>
        /// Whether to skip nuget restore. Nuget is on by default.
        /// </summary>
        public bool SkipNuget { get; set; } = false;
    }
}
