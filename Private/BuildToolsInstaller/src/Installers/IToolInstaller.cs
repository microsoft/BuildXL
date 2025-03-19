// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildToolsInstaller
{
    /// <summary>
    /// A component that knows how to retrieve and install a build tool
    /// </summary>
    internal interface IToolInstaller
    {
        /// <summary>
        /// Install a tool based on the given <see cref="InstallationArguments"/>
        /// </summary>
        public Task<bool> InstallAsync(InstallationArguments args);
    }
}
