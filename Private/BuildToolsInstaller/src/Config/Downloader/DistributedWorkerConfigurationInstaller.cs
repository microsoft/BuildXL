// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildToolsInstaller.Utilities;
using NuGet.Versioning;
using System.Diagnostics.Contracts;

namespace BuildToolsInstaller.Config.Downloader
{
    /// <summary>
    /// Downloads the configuration that is indicated by a corresponding coordinator job running in the pipeline
    /// </summary>
    internal class DistributedWorkerConfigurationDownloader: ConfigurationDownloaderBase
    {
        private const int DefaultWorkerTimeoutMin = 20;
        private const int PropertiesPollDelaySeconds = 5;

        public DistributedWorkerConfigurationDownloader(string? feedOverride, IAdoService adoService, ILogger logger) : base(feedOverride, adoService,logger)
        {
            Contract.Requires(adoService.IsEnabled);
        }

        public async Task<string?> DownloadConfigurationPackageAsync()
        {
            string? resolvedVersion = null;
            NuGetVersion? resolvedNugetVersion = null;
            if (!int.TryParse(Environment.GetEnvironmentVariable("WORKER_TIMEOUT_MINUTES"), out var timeoutMinutes))
            {
                timeoutMinutes = DefaultWorkerTimeoutMin;
            }

            Logger.Info($"The installer is running in worker mode. It will poll the build properties to get the configuration version from the main agent. This operation will time out after {timeoutMinutes} minutes");
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes)); // Cancel automatically after the timeout

            try
            {
                resolvedVersion = await ResolveVersionFromOrchestratorAsync(cts.Token);
                if (resolvedVersion == null)
                {
                    // Errors have been logged
                    return null;
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                Logger.Error($"Timed out waiting for resolved version from orchestrator. Timeout: {timeoutMinutes} min");
                resolvedVersion = null;
            }

            if (!NuGetVersion.TryParse(resolvedVersion, out resolvedNugetVersion))
            {
                Logger.Error($"The orchestrator-provided version package is malformed: {resolvedVersion}.");
                return null;
            }

            Logger.Info($"The orchestrator job provided the version {resolvedVersion} for the configuration package.");
            if (!await Downloader.TryDownloadNugetToDiskAsync(Feed, ConfigurationPackageName, resolvedNugetVersion, DownloadPath, Logger))
            {
                return null;
            }

            return DownloadPath;
        }

        private async Task<string?> ResolveVersionFromOrchestratorAsync(CancellationToken token)
        {
            await Task.Yield();
            while (true)
            {
                token.ThrowIfCancellationRequested();

                var maybeProperty = await AdoService.GetBuildPropertyAsync(AdoService.DistributedCoordinationKey);
                if (maybeProperty != null)
                {
                    // Orchestrator pushes an empty string on error
                    if (maybeProperty == string.Empty)
                    {
                        base.Logger.Error("The orchestrator stage installer encountered an error resolving the version. This installer is running in worker mode and it can't continue.");
                        return null;
                    }

                    // Should have the resolved property
                    return maybeProperty;
                }

                // Wait before polling again
                await Task.Delay(TimeSpan.FromSeconds(PropertiesPollDelaySeconds), token);
            }

        }
    }
}
