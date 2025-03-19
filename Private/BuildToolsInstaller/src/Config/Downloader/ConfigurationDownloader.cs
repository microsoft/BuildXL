// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildToolsInstaller.Utilities;
using NuGet.Versioning;

namespace BuildToolsInstaller.Config.Downloader
{
    internal class ConfigurationDownloader : ConfigurationDownloaderBase
    {
        public ConfigurationDownloader(string? feedOverride, IAdoService adoService, ILogger logger) : base(feedOverride, adoService, logger)
        {
        }

        /// <summary>
        /// Download the latest known version of the configuration package from the central feed
        /// </summary>
        public async Task<string?> DownloadConfigurationPackageAsync(NuGetVersion? versionOverride)
        {
            Logger.Info("Resolving latest version of the configuration package");

            NuGetVersion versionToDownload;
            if (versionOverride != null)
            {
                Logger.Info($"Using user provided version of the configuration package");
                versionToDownload = versionOverride;
            }
            else
            {
                Logger.Info($"Using latest version of the configuration package");
                var latestVersion = await NugetHelper.GetLatestVersionAsync(Feed, ConfigurationPackageName, Logger);
                if (latestVersion == null)
                {
                    Logger.Error("Failed to resolve the latest version of the configuration package");
                    return null;
                }

                versionToDownload = latestVersion;
            }
            
            if (!await Downloader.TryDownloadNugetToDiskAsync(Feed, ConfigurationPackageName, versionToDownload, DownloadPath, Logger))
            {
                return null;
            }

            if (AdoService.IsEnabled)
            {
                // We resolved a version - we should push it to the properties for eventual workers to consume.
                // If the version is null it means we encountered some error above, so push the empty string
                // (the workers must be signalled that there was an error somehow).
                await AdoService.SetBuildPropertyAsync(AdoService.DistributedCoordinationKey, versionToDownload.OriginalVersion ?? string.Empty);
            }

            return DownloadPath;
        }

    }
}
