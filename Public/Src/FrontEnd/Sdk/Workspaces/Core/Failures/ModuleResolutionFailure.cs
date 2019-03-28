// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Workspaces.Core.Failures
{
    /// <summary>
    /// Module resolution failed to complete succesfully due to some previous problem
    /// </summary>
    public sealed class ModuleResolutionFailure : WorkspaceFailure
    {
        /// <inheritdoc/>
        public override string Describe()
        {
            return "Module resolution failed. Root cause of the failure should have already been logged.";
        }
    }
}
