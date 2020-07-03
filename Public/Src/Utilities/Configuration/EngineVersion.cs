// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Engine version for controlling configuration values.
    /// </summary>
    public static class EngineVersion
    {
        /// <summary>
        /// Property name that user can specify to set the engine version, i.e., '/p:BUILDXL_ENGINE_VERSION=X', where X is the version.
        /// </summary>
        public const string PropertyName = "BUILDXL_ENGINE_VERSION";

        /// <summary>
        /// Current engine version.
        /// </summary>
        /// <remarks>
        /// Increment the value when introducing a breaking change due to changing the default values of some configurations.
        /// This value must be monotonically increasing.
        /// For example, suppose that the current engine version is N, and BuildXL introduces a new feature F that breaks a long-term service branch (LTSB) 
        /// because the feature requires some modification on the branch. LTSB is frozen, and so making modification is nearly impossible.
        /// To move forward, do the following:
        ///   - Increment the engine version to N + 1, and fill in the reason below.
        ///   - Ensure that the LTSB invocation pass '/p:BUILDXL_ENGINE_VERSION=X', where X &lt; N + 1.
        ///   - Add in the configuration the following code (whichever appropriate):
        ///       if (EngineVersion.Version &gt; X) { enableF = true; }
        ///     or
        ///       if (EngineVersion.Version &lt; N+1) { disableF = true; }
        /// 
        /// Reasons:
        /// 1: Change the default of IUnsafeSandboxConfiguration.IgnoreCreateProcessReport from true to false.
        /// </remarks>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Engine version specified by user.
        /// </summary>
        private static int? s_usedVersion = default;

        /// <summary>
        /// Gets and sets engine version.
        /// </summary>
        public static int Version
        {
            get => s_usedVersion ?? CurrentVersion;
            set
            {
                s_usedVersion = value;
            }
        }
    }
}
