// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildToolsInstaller.Config;
using BuildToolsInstaller.Config.Downloader;
using BuildToolsInstaller.Installers;
using BuildToolsInstaller.Logging;
using BuildToolsInstaller.Utilities;
using NuGet.Versioning;

namespace BuildToolsInstaller
{
    /// <summary>
    /// Collects the arguments given to the executable
    /// </summary>
    public record struct InstallerArgs(string ToolsDirectory, string ToolsConfigFile, string? GlobalConfigLocation, string? FeedOverride, NuGetVersion? ConfigVersion, bool WorkerMode);

    /// <summary>
    /// Common arguments given to the specific installers
    /// </summary>
    /// <param name="VersionDescriptor">A string that implies a particular version of the tool: this can be a literal version or a ring name</param>
    /// <param name="OutputVariable">The name of the variable that will hold the installation location for this tool</param>
    /// <param name="ToolsDirectory">Where to download the tool</param>
    /// <param name="IgnoreCache">If true, any cached version is ignored and the tool is installed fresh</param>
    /// <param name="FeedOverride">Get packages from this feed instead of the default one</param>
    public record struct InstallationArguments(string? VersionDescriptor, string PackageSelector, string OutputVariable, string ToolsDirectory, bool IgnoreCache, string? FeedOverride);

    /// <summary>
    /// Entrypoint for the installation logic
    /// </summary>
    public sealed class BuildToolsInstaller
    {
        private const int SuccessExitCode = 0;
        private const int FailureExitCode = 1;

        internal static async Task<int> Run(InstallerArgs installerArgs)
        {
            // Deserialize the configuration file from the parameter to a ToolsToInstall object, then install in parallel
            var adoService = AdoService.Instance;
            ILogger logger = adoService.IsEnabled ? new AdoConsoleLogger() : new ConsoleLogger();
            var toolsToInstall = await JsonUtilities.DeserializeAsync<ToolsToInstall>(installerArgs.ToolsConfigFile, logger, default);
            if (toolsToInstall == null)
            {
                // Error should have been logged.
                return FailureExitCode;
            }

            if (installerArgs.WorkerMode && !adoService.IsEnabled)
            {
                logger.Error("Worker mode is only valid when running in ADO builds");
                return FailureExitCode;
            }

            string? configurationDirectory;
            try
            {
                if (installerArgs.WorkerMode)
                {
                    var configDownloader = new DistributedWorkerConfigurationDownloader(installerArgs.FeedOverride, adoService, logger);
                    configurationDirectory = await configDownloader.DownloadConfigurationPackageAsync();
                }
                else
                {
                    var configDownloader = new ConfigurationDownloader(installerArgs.FeedOverride, adoService, logger);
                    configurationDirectory = await configDownloader.DownloadConfigurationPackageAsync(installerArgs.ConfigVersion);
                }
            }
            catch (Exception e)
            {
                logger.Error($"Failed to download configuration: {e.Message}");
                return FailureExitCode;
            }

            if (configurationDirectory == null)
            {
                // Errors should have been logged
                return FailureExitCode;
            }


            var tasks = toolsToInstall.Tools.Select(tool =>
            {
                IToolInstaller installer = new ToolInstaller(tool.Tool, new NugetDownloader(), configurationDirectory, adoService, logger);

                return installer.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = tool.Version,
                    PackageSelector = tool.PackageSelector,
                    OutputVariable = tool.OutputVariable,
                    ToolsDirectory = installerArgs.ToolsDirectory,
                    IgnoreCache = tool.IgnoreCache,
                    FeedOverride = installerArgs.FeedOverride
                });
            });

            var results = await Task.WhenAll(tasks);
            return results.All(r => r) ? SuccessExitCode : FailureExitCode;
        }
    }
}
