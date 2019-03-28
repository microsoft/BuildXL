// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Targets from imported files with before/after targets.
    /// </summary>
    internal class ImportedTargetsWithBeforeAfterTargets
    {
        public ImportedTargetsWithBeforeAfterTargets(string targetName, string beforeTargets, string afterTargets)
        {
            TargetName = targetName;
            BeforeTargets = beforeTargets;
            AfterTargets = afterTargets;
        }

        /// <summary>
        /// Gets the target's name.
        /// </summary>
        public string TargetName { get; }

        /// <summary>
        /// Gets an MSBuild list-string of AfterTargets for this target.
        /// </summary>
        public string AfterTargets { get; }

        /// <summary>
        /// Gets an MSBuild list-string of BeforeTargets for this target.
        /// </summary>
        public string BeforeTargets { get; }
    }
}
