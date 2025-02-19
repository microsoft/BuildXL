// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Contracts;
using BuildToolsInstaller.Config;
using BuildToolsInstaller.Utilities;
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
        protected abstract string PackageName { get; }

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
            if (!await TryInitializeConfigAsync(args.ExtraConfiguration))
            {
                return false;
            }

            try
            {
                var versionDescriptor = args.VersionDescriptor;
                var maybeResolvedVersion = await TryResolveVersionAsync(versionDescriptor);
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
                            Logger.Info($"Skipping download: {PackageName} version {resolvedVersion} already installed at {downloadLocation}.");
                            return currentInstallationStatus;
                        }
                        else if (Path.Exists(downloadLocation))
                        {
                            // We never tracked this installation, but the path exists.
                            // Assume this was placed before we run so this is 'cached'.
                            // TODO: Actual caching logic, when we decide where is it that 
                            // we put the cached versions at image creation time. 
                            Logger.Info($"Skipping download: {PackageName} version {resolvedVersion} available from cached location {downloadLocation}.");
                            return InstallationStatus.InstalledFromCache;
                        }

                        // If we got here, we need to download the tool, so this is a fresh installation
                        var feed = args.FeedOverride ?? InferSourceRepository(AdoService);

                        var repository = CreateSourceRepository(feed);
                        if (await m_downloader.TryDownloadNugetToDiskAsync(repository, PackageName, resolvedVersion, downloadLocation, Logger))
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
                Logger.Error($"Failed trying to download nuget package '{PackageName}' : '{ex}'");
            }

            return false;
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
        /// Construct the implicit source repository for installers, a well-known feed that should be installed in the organization
        /// </summary>
        private static string InferSourceRepository(IAdoService adoService)
        {
            if (!adoService.IsEnabled)
            {
                throw new InvalidOperationException("Automatic source repository inference is only supported when running on an ADO Build");
            }

            if (!adoService.TryGetOrganizationName(out var adoOrganizationName))
            {
                throw new InvalidOperationException("Could not retrieve organization name");
            }

            // This feed is installed in every organization as part of 1ESPT onboarding
            // TODO [maly]: Change Guardian feed to 1ESTools feed
            return $"https://pkgs.dev.azure.com/{adoOrganizationName}/_packaging/Guardian1ESPTUpstreamOrgFeed/nuget/v3/index.json";
        }

        /// <nodoc />
        private static SourceRepository CreateSourceRepository(string feedUrl)
        {
            var packageSource = new PackageSource(feedUrl, "SourceFeed");

            // Because the feed is known (either the well-known mirror or the user-provided override),
            // we can simply use a PAT that we assume will grant the appropriate privileges instead of going through a credential provider.
            packageSource.Credentials = new PackageSourceCredential(feedUrl, "IrrelevantUsername", GetPatFromEnvironment(), true, string.Empty);
            return Repository.Factory.GetCoreV3(packageSource);
        }

        /// <summary>
        /// The installer is given an configuration string, iheritors should this methid to initialize their configuration
        /// based on it
        /// </summary>
        protected abstract Task<bool> TryInitializeConfigAsync(string? extraConfiguration);

        /// <summary>
        /// Given a version descriptor, this method should resolve the version to install from NuGet
        /// </summary>
        protected abstract Task<(NuGetVersion Version, bool IgnoreCache)?> TryResolveVersionAsync(string? versionDescriptor);

        internal string GetDownloadLocation(string toolDirectory, string version) => Path.Combine(toolDirectory, ToolName, version);

        private static string GetPatFromEnvironment()
        {
            return Environment.GetEnvironmentVariable("BUILDTOOLSDOWNLOADER_NUGET_PAT") ?? Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN") ?? "";
        }
    }
}
