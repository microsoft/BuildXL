// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildToolsInstaller.Logging;
using BuildToolsInstaller.Utiltiies;

namespace BuildToolsInstaller
{
    public record struct BuildToolsInstallerArgs(BuildTool Tool, string ToolsDirectory, string? ConfigFilePath, bool ForceInstallation);

    /// <summary>
    /// Entrypoint for the installation logic
    /// </summary>
    public sealed class BuildToolsInstaller
    {
        private const int SuccessExitCode = 0;
        private const int FailureExitCode = 1;

        /// <summary>
        /// Pick an installer based on the arguments and run it
        /// </summary>
        public static async Task<int> Run(BuildToolsInstallerArgs arguments)
        {
            // First, detect if we are running in an ADO build.
            // This tool is meant to be run on a build, but is also run at image-creation
            ILogger logger = AdoUtilities.IsAdoBuild ? new AdoConsoleLogger() : new ConsoleLogger();

            IToolInstaller installer = arguments.Tool switch
            {
                BuildTool.BuildXL => new BuildXLNugetInstaller(new NugetDownloader(), logger),

                // Shouldn't happen - the argument comes from a TryParse that should have failed earlier
                _ => throw new NotImplementedException($"No tool installer for tool {arguments.Tool}"),
            };

            return await installer.InstallAsync(arguments) ? SuccessExitCode : FailureExitCode;
        }
    }
}
