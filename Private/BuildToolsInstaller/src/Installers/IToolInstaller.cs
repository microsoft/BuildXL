// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildToolsInstaller
{
    /// <summary>
    /// A component that knows how to retrieve and install a <see cref="BuildTool"/>
    /// </summary>
    internal interface IToolInstaller
    {
        /// <summary>
        /// Install to the given directory
        /// </summary>
        public Task<bool> InstallAsync(BuildToolsInstallerArgs args);
    }
}
