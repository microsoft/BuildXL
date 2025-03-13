// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildToolsInstaller.Config;
using BuildToolsInstaller.Logging;
using BuildToolsInstaller.Utilities;
using NuGet.Common;

namespace BuildToolsInstaller
{
    /// <summary>
    /// Collects the arguments given to the executable
    /// </summary>
    public record struct InstallerArgs(string ToolsDirectory, string ToolsConfigFile, string? GlobalConfigLocation, string? FeedOverride);

    /// <summary>
    /// Arguments for 'single-tool' mode, which is in use by some customers but in road to deprecation
    /// </summary>
    public record struct SingleToolInstallerArgs(BuildTool Tool, string? VersionDescriptor, string ToolsDirectory, string? ConfigFilePath, bool IgnoreCache, string? FeedOverride);

    /// <summary>
    /// Common arguments given to the specific installers
    /// </summary>
    /// <param name="VersionDescriptor">A string that implies a particular version of the tool: this can be a literal version or a ring name</param>
    /// <param name="OutputVariable">The name of the variable that will hold the installation location for this tool</param>
    /// <param name="ExtraConfiguration">A string containing an arbitrary payload to the installer</param>
    /// <param name="ToolsDirectory">Where to download the tool</param>
    /// <param name="IgnoreCache">If true, any cached version is ignored and the tool is installed fresh</param>
    /// <param name="FeedOverride">Get packages from this feed instead of the default one</param>
    public record struct InstallationArguments(string? VersionDescriptor, string PackageSelector, string OutputVariable, string? ExtraConfiguration, string ToolsDirectory, bool IgnoreCache, string? FeedOverride);

    /// <summary>
    /// Entrypoint for the installation logic
    /// </summary>
    public sealed class BuildToolsInstaller
    {
        private const int SuccessExitCode = 0;
        private const int FailureExitCode = 1;
        private const string ConfigurationPackageName = "Tools.Config";

        /// <summary>
        /// Pick an installer based on the arguments and run it
        /// </summary>
        public static async Task<int> Run(SingleToolInstallerArgs arguments)
        {
            // This tool is primarily run on ADO, but could be also run locally, so we switch on IsEnabled when
            // we want to do ADO-specific operations.
            var adoService = AdoService.Instance;
            ILogger logger = adoService.IsEnabled ? new AdoConsoleLogger() : new ConsoleLogger();

            string? configurationDirectory;
            try
            {
                configurationDirectory = await DownloadConfigurationAsync(adoService, arguments.FeedOverride, logger);
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

            IToolInstaller installer = SelectInstaller(arguments.Tool, configurationDirectory, adoService, logger);
            return await installer.InstallAsync(new InstallationArguments()
            {
                VersionDescriptor = arguments.VersionDescriptor,
                OutputVariable = installer.DefaultToolLocationVariable,
                PackageSelector = OperatingSystem.IsWindows() ? "Windows" : "Linux",
                ExtraConfiguration = arguments.ConfigFilePath,
                ToolsDirectory = arguments.ToolsDirectory,
                IgnoreCache = arguments.IgnoreCache,
                FeedOverride = arguments.FeedOverride
            }) ? SuccessExitCode : FailureExitCode;
        }

        private static IToolInstaller SelectInstaller(BuildTool tool, string configDirectory, AdoService adoService, ILogger logger)
        {
            return tool switch
            {
                BuildTool.BuildXL => new BuildXLNugetInstaller(new NugetDownloader(), configDirectory, adoService, logger),

                // Shouldn't happen - the argument comes from a TryParse that should have failed earlier
                _ => throw new NotImplementedException($"No tool installer for tool {tool}"),
            };
        }

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

            string? configurationDirectory;
            try
            {
                configurationDirectory = await DownloadConfigurationAsync(adoService, installerArgs.FeedOverride, logger);
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

            var tasks = toolsToInstall.Tools.Select(async tool =>
            {
                var installer = SelectInstaller(tool.Tool, configurationDirectory,  adoService, logger);
                return await installer.InstallAsync(new InstallationArguments()
                {
                    VersionDescriptor = tool.Version,
                    PackageSelector = tool.PackageSelector,
                    OutputVariable = tool.OutputVariable,
                    ExtraConfiguration = tool.AdditionalConfiguration,
                    ToolsDirectory = installerArgs.ToolsDirectory,
                    IgnoreCache = tool.IgnoreCache,
                    FeedOverride = installerArgs.FeedOverride
                });
            });

            var results = await Task.WhenAll(tasks);
            return results.All(r => r) ? SuccessExitCode : FailureExitCode;
        }

        /// <summary>
        /// Download the latest known version of the configuration package from the central feed
        /// </summary>
        private static async Task<string?> DownloadConfigurationAsync(AdoService service, string? feedOverride, ILogger logger)
        {
            var downloadPath  = Path.Combine(Path.GetTempPath(), "downloaded_config");
            logger.Info("Resolving latest version of the configuration package");
            var upstream = NugetHelper.CreateSourceRepository(feedOverride ?? NugetHelper.InferSourceRepository(service));
            var configVersion = await NugetHelper.GetLatestVersionAsync(upstream, ConfigurationPackageName, logger);
            if (configVersion == null)
            {
                logger.Error("Failed to resolve the latest version of the configuration package");
                return null;
            }

            var downloader = new NugetDownloader();
            if (!await downloader.TryDownloadNugetToDiskAsync(upstream, ConfigurationPackageName, configVersion, downloadPath, logger))
            {
                return null;
            }

            return downloadPath;
        }
    }
}
