// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildToolsInstaller.Config;
using BuildToolsInstaller.Logging;
using BuildToolsInstaller.Utiltiies;
using NuGet.Common;

namespace BuildToolsInstaller
{
    public record struct BuildToolsInstallerArgs(BuildTool Tool, string? Ring, string ToolsDirectory, string? ConfigFilePath, bool ForceInstallation);

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
            // This tool is primarily run on ADO, but could be also run locally, so we switch on IsEnabled when
            // we want to do ADO-specific operations.
            var adoService = AdoService.Instance;
            ILogger logger = adoService.IsEnabled ? new AdoConsoleLogger() : new ConsoleLogger();

            const string ConfigurationWellKnownUri = "https://bxlscripts.z20.web.core.windows.net/config/DeploymentConfig_V0.json";
            var deploymentConfiguration = await JsonUtilities.DeserializeFromHttpAsync<DeploymentConfiguration>(new Uri(ConfigurationWellKnownUri), logger, default);
            if (deploymentConfiguration == null)
            {
                // Error should have been logged.
                return FailureExitCode;
            }

            IToolInstaller installer = arguments.Tool switch
            {
                BuildTool.BuildXL => new BuildXLNugetInstaller(new NugetDownloader(), adoService, logger),

                // Shouldn't happen - the argument comes from a TryParse that should have failed earlier
                _ => throw new NotImplementedException($"No tool installer for tool {arguments.Tool}"),
            };

            var selectedRing = arguments.Ring ?? installer.DefaultRing;
            var resolvedVersion = ConfigurationUtilities.ResolveVersion(deploymentConfiguration, selectedRing, arguments.Tool, adoService, logger);
            if (resolvedVersion == null)
            {
                logger.Error("Failed to resolve version to install. Installation has failed.");
                return FailureExitCode;
            }
            
            return await installer.InstallAsync(resolvedVersion, arguments) ? SuccessExitCode : FailureExitCode;
        }
    }
}
