// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Contracts;
using System.Text.Json;
using BuildToolsInstaller.Config;
using BuildToolsInstaller.Utilities;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BuildToolsInstaller.Installers
{
    public abstract class CentralNugetFeedInstallerBase : IToolInstaller
    {
        protected readonly IAdoService AdoService;
        private readonly INugetDownloader m_downloader;
        protected readonly string ConfigDirectory;
        protected readonly ILogger Logger;

        /// <summary>
        /// The name of the package to install
        /// </summary>
        protected DeploymentConfiguration DeploymentConfig = null!;

        /// <summary>
        /// A display name for the tool. Used in the installation paths, logging, etc.
        /// </summary>
        protected string ToolName => Tool.ToString();

        protected abstract BuildTool Tool { get; }

        public abstract string DefaultToolLocationVariable { get; }

        public CentralNugetFeedInstallerBase(INugetDownloader downloader, string configDirectory, IAdoService adoService, ILogger logger)
        {
            AdoService = adoService;
            m_downloader = downloader;
            ConfigDirectory = configDirectory;
            Logger = logger;
        }

        /// <inheritdoc />
        public async Task<bool> InstallAsync(InstallationArguments args)
        {
            await Task.Yield();

            if (!await InitializeGlobalConfiguration(args))
            {
                return false;
            }

            if (!await TryInitializeConfigAsync(args.ExtraConfiguration))
            {
                return false;
            }

            if (!DeploymentConfig.Packages.TryGetValue(args.PackageSelector, out string? packageName))
            {
                Logger.Error($"The package for tool {ToolName} with selector '{args.PackageSelector}' not found in the configuration. Installation can't proceed.");
                return false;
            }

            try
            {
                var versionDescriptor = args.VersionDescriptor;
                var maybeResolvedVersion = await TryResolveVersionAsync(packageName, versionDescriptor);

                if (maybeResolvedVersion == null)
                {
                    // Error should have been logged
                    return false;
                }

                (var resolvedVersion, var ignoreCache) = maybeResolvedVersion.Value;

                // The download location is based on the resolved version, but the variable we set to advertise
                // the location is based on the version descriptor. 
                // Because different version descriptors might resolve to the same version, but require 
                // different output variables, this means that we might 'want' to download the exact same package version twice
                // To prevent races, we perform the download through InstallationDirectoryLock.PerformInstallationAction
                var downloadLocation = GetDownloadLocation(args.ToolsDirectory, resolvedVersion.OriginalVersion!);
                var installationResult = await InstallationDirectoryLock.Instance.PerformInstallationAction(downloadLocation,
                    async currentInstallationStatus =>
                    {
                        if (args.IgnoreCache && currentInstallationStatus != InstallationStatus.FreshInstall)
                        {
                            Logger.Info($"Installation is forced. Deleting {downloadLocation} and re-installing.");

                            // Delete the contents of the installation directory and continue
                            if (!TryDeleteInstallationDirectory(downloadLocation))
                            {
                                throw new Exception("Couldn't delete installation directory. Can't continue with the installation.");
                            }
                        }
                        else if (currentInstallationStatus != InstallationStatus.None)
                        {
                            // We already installed it in this run. Skip the download.
                            Logger.Info($"Skipping download: {packageName} version {resolvedVersion} already installed at {downloadLocation}.");
                            return currentInstallationStatus;
                        }
                        else if (Path.Exists(downloadLocation))
                        {
                            // We never tracked this installation, but the path exists.
                            // Assume this was placed before we run so this is 'cached'.
                            // TODO: Actual caching logic, when we decide where is it that 
                            // we put the cached versions at image creation time. 
                            Logger.Info($"Skipping download: {packageName} version {resolvedVersion} available from cached location {downloadLocation}.");
                            return InstallationStatus.InstalledFromCache;
                        }

                        // If we got here, we need to download the tool, so this is a fresh installation
                        var feed = args.FeedOverride ?? NugetHelper.InferSourceRepository(AdoService);

                        var repository = NugetHelper.CreateSourceRepository(feed);
                        if (await m_downloader.TryDownloadNugetToDiskAsync(repository, packageName, resolvedVersion, downloadLocation, Logger))
                        {
                            return InstallationStatus.FreshInstall;
                        }
                        else
                        {
                            throw new Exception("Installation failed. Details should be in the logs.");
                        }
                    }
                , Logger);

                if (installationResult == InstallationStatus.None)
                {
                    Logger.Error("Installation failed. Additional details should have been logged.");
                    return false;
                }

                // Installation was successful, so now we can set the variable that will be consumed by subsequent tasks
                if (AdoService.IsEnabled)
                {
                    AdoService.SetVariable(args.OutputVariable, downloadLocation);
                }
                else
                {
                    Environment.SetEnvironmentVariable(args.OutputVariable, downloadLocation);
                }

                // The requested location 
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed trying to download nuget package '{packageName}' : '{ex}'");
            }

            return false;
        }

        private async Task<bool> InitializeGlobalConfiguration(InstallationArguments args)
        {
            var ringConfigPath = Path.Combine(ConfigurationUtilities.GetConfigurationPathForTool(ConfigDirectory, Tool), "deployment-config.json");
            var serializerOptions = new JsonSerializerOptions(JsonUtilities.DefaultSerializerOptions)
            {
                Converters = { new CaseInsensitiveDictionaryConverter<string>() }
            };
            var maybeDeploymentConfig = await JsonUtilities.DeserializeAsync<DeploymentConfiguration>(ringConfigPath, Logger, serializerOptions);
            if (maybeDeploymentConfig == null)
            {
                Logger.Error("Failed to load deployment configuration. Installation has failed.");
                return false;
            }

            DeploymentConfig = maybeDeploymentConfig;
            return true;
        }

        private bool TryDeleteInstallationDirectory(string engineLocation)
        {
            try
            {
                if (!Directory.Exists(engineLocation))
                {
                    return true;
                }

                Directory.Delete(engineLocation, true);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Couldn't delete pre-existing installation directory {engineLocation}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// The installer is given an configuration string, iheritors should this methid to initialize their configuration
        /// based on it
        /// </summary>
        protected abstract Task<bool> TryInitializeConfigAsync(string? extraConfiguration);

        /// <summary>
        /// Given a version descriptor, this method should resolve the version to install from NuGet
        /// </summary>
        protected abstract Task<(NuGetVersion Version, bool IgnoreCache)?> TryResolveVersionAsync(string packageName, string? versionDescriptor);

        internal string GetDownloadLocation(string toolDirectory, string version) => Path.Combine(toolDirectory, ToolName, version);
    }
}
