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
        /// Install a tool based on the given <see cref="InstallationArguments"/>
        /// </summary>
        public Task<bool> InstallAsync(InstallationArguments args);

        // TODO [maly]: Deprecate this! This is used only in the non-parallel install case, where callers don't provide the output variable
        public string DefaultToolLocationVariable { get; }
    }
}
